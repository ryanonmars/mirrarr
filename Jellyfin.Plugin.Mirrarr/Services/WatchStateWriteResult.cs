namespace Jellyfin.Plugin.Mirrarr.Services;

public enum WatchStateWriteResult
{
    Updated,
    Unchanged,
    MissingUser,
    Failed,
}
