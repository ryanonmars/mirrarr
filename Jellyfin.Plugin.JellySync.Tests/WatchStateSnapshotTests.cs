using MediaBrowser.Controller.Entities;
using Jellyfin.Plugin.JellySync.Services;

namespace Jellyfin.Plugin.JellySync.Tests;

public class WatchStateSnapshotTests
{
    [Fact]
    public void From_user_item_data_copies_watch_state_fields()
    {
        var lastPlayed = new DateTime(2026, 8, 29, 14, 30, 0, DateTimeKind.Utc);
        var source = new UserItemData
        {
            Key = "source",
            Played = true,
            PlayCount = 3,
            LastPlayedDate = lastPlayed,
            PlaybackPositionTicks = 123456789,
        };

        var snapshot = WatchStateSnapshot.From(source);

        Assert.Equal(new WatchStateSnapshot(true, 3, lastPlayed, 123456789), snapshot);
    }

    [Fact]
    public void Apply_to_updates_only_watch_state_fields()
    {
        var lastPlayed = new DateTime(2026, 8, 29, 14, 30, 0, DateTimeKind.Utc);
        var target = new UserItemData
        {
            Key = "target",
            Played = false,
            PlayCount = 1,
            LastPlayedDate = null,
            PlaybackPositionTicks = 7,
            IsFavorite = true,
            Rating = 9.5,
        };
        var snapshot = new WatchStateSnapshot(true, 3, lastPlayed, 123456789);

        snapshot.ApplyTo(target);

        Assert.True(target.Played);
        Assert.Equal(3, target.PlayCount);
        Assert.Equal(lastPlayed, target.LastPlayedDate);
        Assert.Equal(123456789, target.PlaybackPositionTicks);
        Assert.True(target.IsFavorite);
        Assert.Equal(9.5, target.Rating);
        Assert.Equal("target", target.Key);
    }
}
