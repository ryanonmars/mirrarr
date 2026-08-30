using Jellyfin.Plugin.JellySync.Services;

namespace Jellyfin.Plugin.JellySync.Tests;

public class SyncWorkQueueTests
{
    [Fact]
    public async Task Cancellation_prevents_the_next_buffered_work_item_from_starting()
    {
        var queue = new SyncWorkQueue();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;
        using var cancellation = new CancellationTokenSource();
        queue.TryEnqueue(new DelegateWorkItem(async _ =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
        }));
        queue.TryEnqueue(new DelegateWorkItem(_ =>
        {
            secondStarted = true;
            return Task.CompletedTask;
        }));
        queue.Complete();

        var worker = queue.ProcessAsync(cancellation.Token);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        releaseFirst.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker);
        Assert.False(secondStarted);
    }

    private sealed class DelegateWorkItem(Func<CancellationToken, Task> process) : ISyncWorkItem
    {
        public Task ProcessAsync(CancellationToken cancellationToken) => process(cancellationToken);
    }
}
