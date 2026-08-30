using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellySync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.JellySync.Tests;

public class WatchStateWriterTests
{
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Distinguishes_updated_unchanged_missing_and_failed_results()
    {
        var user = new User("target", "Jellyfin", "Jellyfin") { Id = UserId };
        var item = new TestMovie { Id = Guid.Parse("66666666-6666-6666-6666-666666666666") };
        var userManager = new Mock<IUserManager>(MockBehavior.Strict);
        var dataManager = new Mock<IUserDataManager>(MockBehavior.Strict);
        var writer = new WatchStateWriter(dataManager.Object, userManager.Object, NullLogger<WatchStateWriter>.Instance);
        var snapshot = new WatchStateSnapshot(true, 1, null, 10);

        userManager.Setup(manager => manager.GetUserById(UserId)).Returns((User?)null);
        Assert.Equal(WatchStateWriteResult.MissingUser, writer.Apply(UserId, item, snapshot, CancellationToken.None));

        userManager.Setup(manager => manager.GetUserById(UserId)).Returns(user);
        dataManager.Setup(manager => manager.GetUserData(user, item)).Returns(new UserItemData
        {
            Key = "existing",
            Played = true,
            PlayCount = 1,
            PlaybackPositionTicks = 10,
        });
        Assert.Equal(WatchStateWriteResult.Unchanged, writer.Apply(UserId, item, snapshot, CancellationToken.None));

        var target = new UserItemData { Key = "private-key", IsFavorite = true, Rating = 9 };
        dataManager.Setup(manager => manager.GetUserData(user, item)).Returns(target);
        dataManager.Setup(manager => manager.SaveUserData(user, item, target, UserDataSaveReason.Import, It.IsAny<CancellationToken>()));
        Assert.Equal(WatchStateWriteResult.Updated, writer.Apply(UserId, item, snapshot, CancellationToken.None));
        Assert.True(target.IsFavorite);
        Assert.Equal(9, target.Rating);
        Assert.Equal("private-key", target.Key);

        dataManager.Setup(manager => manager.GetUserData(user, item)).Throws(new InvalidOperationException("database unavailable"));
        Assert.Equal(WatchStateWriteResult.Failed, writer.Apply(UserId, item, snapshot, CancellationToken.None));
    }

    [Fact]
    public void Rethrows_worker_cancellation()
    {
        var userManager = new Mock<IUserManager>(MockBehavior.Strict);
        var dataManager = new Mock<IUserDataManager>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var writer = new WatchStateWriter(dataManager.Object, userManager.Object, NullLogger<WatchStateWriter>.Instance);

        Assert.ThrowsAny<OperationCanceledException>(() => writer.Apply(UserId, new TestMovie(), new WatchStateSnapshot(false, 0, null, 0), cancellation.Token));
    }
}
