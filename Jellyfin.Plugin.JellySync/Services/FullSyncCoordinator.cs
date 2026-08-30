using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellySync.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySync.Services;

public sealed class FullSyncCoordinator : IFullSyncCoordinator
{
    private static readonly WatchStateSnapshot DefaultSnapshot = new(false, 0, null, 0);
    private readonly object _statusLock = new();
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly WatchStateWriter _writer;
    private readonly ISyncWorkQueue _workQueue;
    private readonly IPluginConfigurationProvider _configurationProvider;
    private readonly ILogger<FullSyncCoordinator> _logger;
    private FullSyncStatus _status = FullSyncStatus.Idle;

    public FullSyncCoordinator(
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILibraryManager libraryManager,
        WatchStateWriter writer,
        ISyncWorkQueue workQueue,
        IPluginConfigurationProvider configurationProvider,
        ILogger<FullSyncCoordinator> logger)
    {
        _userDataManager = userDataManager;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _writer = writer;
        _workQueue = workQueue;
        _configurationProvider = configurationProvider;
        _logger = logger;
    }

    public FullSyncStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
    }

    public FullSyncStartResult Start(Guid sourceUserId)
    {
        if (sourceUserId == Guid.Empty)
        {
            return Result(FullSyncStartOutcome.InvalidRequest, "A non-empty sourceUserId is required.");
        }

        var configuration = _configurationProvider.GetConfiguration();
        if (configuration is null || !configuration.Enabled)
        {
            return Result(FullSyncStartOutcome.InvalidConfiguration, "JellySync must be enabled before starting a full sync.");
        }

        var validation = ConfigurationValidator.Validate(configuration);
        if (!validation.IsValid)
        {
            return Result(FullSyncStartOutcome.InvalidConfiguration, string.Join(" ", validation.Errors));
        }

        var selectedUsers = configuration.UserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (!selectedUsers.Contains(sourceUserId) || _userManager.GetUserById(sourceUserId) is null)
        {
            return Result(FullSyncStartOutcome.InvalidSource, "The source user must be an existing configured JellySync user.");
        }

        var job = new FullSyncJob(
            sourceUserId,
            selectedUsers.Where(id => id != sourceUserId).ToArray(),
            configuration.IncludeAllLibraries,
            configuration.LibraryIds.Where(id => id != Guid.Empty).Distinct().ToArray());

        lock (_statusLock)
        {
            if (_status.State is "Queued" or "Running")
            {
                return new FullSyncStartResult(FullSyncStartOutcome.AlreadyActive, "A full sync is already queued or running.", _status);
            }

            _status = new FullSyncStatus(
                "Queued",
                sourceUserId,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                null);
        }

        if (!_workQueue.TryEnqueue(new FullSyncWorkItem(this, job)))
        {
            lock (_statusLock)
            {
                if (_status.State == "Queued" && _status.SourceUserId == sourceUserId)
                {
                    _status = FullSyncStatus.Idle;
                }
            }

            return Result(FullSyncStartOutcome.WorkerUnavailable, "The synchronization worker is stopping and cannot accept new work.");
        }

        return Result(FullSyncStartOutcome.Accepted, "Full sync queued.");
    }

    private FullSyncStartResult Result(FullSyncStartOutcome outcome, string message) => new(outcome, message, Status);

    private Task RunAsync(FullSyncJob job, CancellationToken cancellationToken)
    {
        SetStatus(status => status with
        {
            State = "Running",
            StartedUtc = DateTime.UtcNow,
        });

        try
        {
            var items = GetItems(job);
            SetStatus(status => status with { TotalItems = items.Count });

            var sourceUser = _userManager.GetUserById(job.SourceUserId)
                ?? throw new InvalidOperationException("The selected source user no longer exists.");

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WatchStateSnapshot snapshot;
                try
                {
                    var sourceData = _userDataManager.GetUserData(sourceUser, item);
                    snapshot = sourceData is null ? DefaultSnapshot : WatchStateSnapshot.From(sourceData);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Unable to read source watch state for full-sync item {ItemId}.", item.Id);
                    SetStatus(status => status with
                    {
                        ProcessedItems = status.ProcessedItems + 1,
                        FailedWrites = status.FailedWrites + job.TargetUserIds.Length,
                        LatestError = exception.Message,
                    });
                    continue;
                }

                foreach (var targetUserId in job.TargetUserIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = _writer.Apply(targetUserId, item, snapshot, cancellationToken);
                    SetStatus(status => result switch
                    {
                        WatchStateWriteResult.Updated => status with { UpdatedWrites = status.UpdatedWrites + 1 },
                        WatchStateWriteResult.Unchanged => status with { UnchangedWrites = status.UnchangedWrites + 1 },
                        WatchStateWriteResult.MissingUser => status with
                        {
                            FailedWrites = status.FailedWrites + 1,
                            LatestError = $"Target user {targetUserId} no longer exists.",
                        },
                        _ => status with
                        {
                            FailedWrites = status.FailedWrites + 1,
                            LatestError = $"Failed to update target user {targetUserId} for item {item.Id}.",
                        },
                    });
                }

                SetStatus(status => status with { ProcessedItems = status.ProcessedItems + 1 });
            }

            SetStatus(status => status with
            {
                State = "Completed",
                CompletedUtc = DateTime.UtcNow,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Full sync failed.");
            SetStatus(status => status with
            {
                State = "Failed",
                CompletedUtc = DateTime.UtcNow,
                LatestError = exception.Message,
            });
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<BaseItem> GetItems(FullSyncJob job)
    {
        if (job.IncludeAllLibraries)
        {
            return _libraryManager.GetItemList(CreateItemQuery());
        }

        return job.LibraryIds
            .SelectMany(libraryId => _libraryManager.GetItemList(CreateItemQuery(libraryId)))
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .ToArray();
    }

    private static InternalItemsQuery CreateItemQuery(Guid? parentId = null) => new()
    {
        Recursive = true,
        IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
        ParentId = parentId ?? Guid.Empty,
    };

    private void SetStatus(Func<FullSyncStatus, FullSyncStatus> update)
    {
        lock (_statusLock)
        {
            _status = update(_status);
        }
    }

    private sealed record FullSyncJob(
        Guid SourceUserId,
        Guid[] TargetUserIds,
        bool IncludeAllLibraries,
        Guid[] LibraryIds);

    private sealed class FullSyncWorkItem(FullSyncCoordinator coordinator, FullSyncJob job) : ISyncWorkItem
    {
        public Task ProcessAsync(CancellationToken cancellationToken) => coordinator.RunAsync(job, cancellationToken);
    }
}
