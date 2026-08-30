namespace Jellyfin.Plugin.Mirrarr.Services;

public interface ISyncWorkQueue
{
    bool TryEnqueue(ISyncWorkItem workItem);

    Task ProcessAsync(CancellationToken cancellationToken);

    void Complete();
}
