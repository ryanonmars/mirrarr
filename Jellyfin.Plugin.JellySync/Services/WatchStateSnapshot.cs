using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.JellySync.Services;

public sealed record WatchStateSnapshot(
    bool Played,
    int PlayCount,
    DateTime? LastPlayedDate,
    long PlaybackPositionTicks)
{
    public static WatchStateSnapshot From(UserItemData source) =>
        new(source.Played, source.PlayCount, source.LastPlayedDate, source.PlaybackPositionTicks);

    public void ApplyTo(UserItemData target)
    {
        target.Played = Played;
        target.PlayCount = PlayCount;
        target.LastPlayedDate = LastPlayedDate;
        target.PlaybackPositionTicks = PlaybackPositionTicks;
    }
}
