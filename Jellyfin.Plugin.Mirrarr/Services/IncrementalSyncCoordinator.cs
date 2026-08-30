using Jellyfin.Plugin.Mirrarr.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Mirrarr.Services;

public sealed class IncrementalSyncCoordinator
{
    private readonly ILibraryManager _libraryManager;
    private readonly WatchStateWriter _writer;
    private readonly ISyncWorkQueue _workQueue;
    private readonly IPluginConfigurationProvider _configurationProvider;
    private readonly ILogger<IncrementalSyncCoordinator> _logger;

    public IncrementalSyncCoordinator(
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILibraryManager libraryManager,
        ISyncWorkQueue workQueue,
        IPluginConfigurationProvider configurationProvider,
        ILogger<IncrementalSyncCoordinator> logger)
        : this(
            libraryManager,
            new WatchStateWriter(userDataManager, userManager, NullLogger<WatchStateWriter>.Instance),
            workQueue,
            configurationProvider,
            logger)
    {
    }

    public IncrementalSyncCoordinator(
        ILibraryManager libraryManager,
        WatchStateWriter writer,
        ISyncWorkQueue workQueue,
        IPluginConfigurationProvider configurationProvider,
        ILogger<IncrementalSyncCoordinator> logger)
    {
        _libraryManager = libraryManager;
        _writer = writer;
        _workQueue = workQueue;
        _configurationProvider = configurationProvider;
        _logger = logger;
    }

    public void HandleUserDataSaved(UserDataSaveEventArgs eventArgs)
    {
        var configuration = _configurationProvider.GetConfiguration();
        if (!ShouldEnqueue(eventArgs, configuration))
        {
            return;
        }

        var workItem = new IncrementalSyncWorkItem(
            eventArgs.UserId,
            eventArgs.Item,
            WatchStateSnapshot.From(eventArgs.UserData));

        if (!_workQueue.TryEnqueue(new IncrementalSyncWork(workItem, this)))
        {
            _logger.LogDebug("Skipped incremental sync because the worker is stopping.");
        }
    }

    public Task ProcessQueueAsync(CancellationToken cancellationToken) => _workQueue.ProcessAsync(cancellationToken);

    public void Complete() => _workQueue.Complete();

    private bool ShouldEnqueue(UserDataSaveEventArgs eventArgs, PluginConfiguration? configuration)
    {
        if (configuration is null || !configuration.Enabled || !ConfigurationValidator.Validate(configuration).IsValid)
        {
            return false;
        }

        if (eventArgs.SaveReason == UserDataSaveReason.Import || !configuration.UserIds.Contains(eventArgs.UserId))
        {
            return false;
        }

        if (eventArgs.Item is not Movie && eventArgs.Item is not Episode)
        {
            return false;
        }

        if (configuration.IncludeAllLibraries)
        {
            return true;
        }

        try
        {
            var selectedLibraries = configuration.LibraryIds.Where(id => id != Guid.Empty).ToHashSet();
            return _libraryManager.GetCollectionFolders(eventArgs.Item).Any(folder => selectedLibraries.Contains(folder.Id));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to resolve collection folders for incremental sync item {ItemId}.", eventArgs.Item.Id);
            return false;
        }
    }

    private Task ApplyAsync(IncrementalSyncWorkItem workItem, CancellationToken cancellationToken)
    {
        var configuration = _configurationProvider.GetConfiguration();
        if (configuration is null || !configuration.Enabled || !ConfigurationValidator.Validate(configuration).IsValid)
        {
            return Task.CompletedTask;
        }

        foreach (var targetUserId in configuration.UserIds.Where(id => id != Guid.Empty && id != workItem.SourceUserId).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();

            _writer.Apply(targetUserId, workItem.Item, workItem.Snapshot, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private sealed class IncrementalSyncWork(IncrementalSyncWorkItem workItem, IncrementalSyncCoordinator coordinator) : ISyncWorkItem
    {
        public Task ProcessAsync(CancellationToken cancellationToken) => coordinator.ApplyAsync(workItem, cancellationToken);
    }
}
