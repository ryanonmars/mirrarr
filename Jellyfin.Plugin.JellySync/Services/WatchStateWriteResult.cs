namespace Jellyfin.Plugin.JellySync.Services;

public enum WatchStateWriteResult
{
    Updated,
    Unchanged,
    MissingUser,
    Failed,
}
