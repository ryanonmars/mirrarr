using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySync.Services;

public sealed class IncrementalSyncHostedService : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly IncrementalSyncCoordinator _coordinator;
    private readonly ILogger<IncrementalSyncHostedService> _logger;
    private Task? _workerTask;
    private CancellationTokenSource? _workerCancellation;
    private int _started;

    public IncrementalSyncHostedService(
        IUserDataManager userDataManager,
        IncrementalSyncCoordinator coordinator,
        ILogger<IncrementalSyncHostedService> logger)
    {
        _userDataManager = userDataManager;
        _coordinator = coordinator;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        _workerCancellation = new CancellationTokenSource();
        _workerTask = _coordinator.ProcessQueueAsync(_workerCancellation.Token);
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _coordinator.Complete();

        if (_workerTask is null)
        {
            return;
        }

        var disposeWorkerCancellation = true;
        try
        {
            await _workerTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Incremental sync shutdown timed out before queued work drained.");
            disposeWorkerCancellation = false;
            _workerCancellation?.Cancel();
            ObserveWorkerCompletion(_workerTask, _workerCancellation);
        }
        finally
        {
            if (disposeWorkerCancellation)
            {
                _workerCancellation?.Dispose();
            }
        }
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs eventArgs) => _coordinator.HandleUserDataSaved(eventArgs);

    private void ObserveWorkerCompletion(Task workerTask, CancellationTokenSource? workerCancellation)
    {
        _ = workerTask.ContinueWith(
            completedTask =>
            {
                try
                {
                    if (completedTask.IsFaulted)
                    {
                        _logger.LogError(completedTask.Exception, "Incremental sync worker faulted after shutdown deadline expired.");
                    }
                    else if (completedTask.IsCanceled)
                    {
                        _logger.LogDebug("Incremental sync worker cancelled after shutdown deadline expired.");
                    }
                }
                finally
                {
                    workerCancellation?.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
