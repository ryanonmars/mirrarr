namespace Jellyfin.Plugin.JellySync.Services;

public interface ISyncWorkItem
{
    Task ProcessAsync(CancellationToken cancellationToken);
}
