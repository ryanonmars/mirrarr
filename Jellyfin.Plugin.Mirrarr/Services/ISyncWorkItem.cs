namespace Jellyfin.Plugin.Mirrarr.Services;

public interface ISyncWorkItem
{
    Task ProcessAsync(CancellationToken cancellationToken);
}
