using System.Threading.Channels;

namespace Jellyfin.Plugin.JellySync.Services;

public sealed class SyncWorkQueue : ISyncWorkQueue
{
    private readonly Channel<ISyncWorkItem> _channel = Channel.CreateUnbounded<ISyncWorkItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public bool TryEnqueue(ISyncWorkItem workItem) => _channel.Writer.TryWrite(workItem);

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var workItem in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await workItem.ProcessAsync(cancellationToken);
        }
    }

    public void Complete() => _channel.Writer.TryComplete();
}
