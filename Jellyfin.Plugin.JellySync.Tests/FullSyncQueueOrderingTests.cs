using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellySync.Configuration;
using Jellyfin.Plugin.JellySync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.JellySync.Tests;

public class FullSyncQueueOrderingTests
{
    [Fact]
    public async Task Incremental_event_enqueued_during_full_sync_runs_after_the_full_job()
    {
        var sourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var sourceUser = new User("source", "Jellyfin", "Jellyfin") { Id = sourceId };
        var targetUser = new User("target", "Jellyfin", "Jellyfin") { Id = targetId };
        var item = new TestMovie { Id = Guid.Parse("66666666-6666-6666-6666-666666666666") };
        var configuration = new PluginConfiguration { Enabled = true, UserIds = [sourceId, targetId] };
        var configurationProvider = new TestConfigurationProvider(configuration);
        var queue = new SyncWorkQueue();
        var userManager = new Mock<IUserManager>(MockBehavior.Strict);
        var userDataManager = new Mock<IUserDataManager>(MockBehavior.Strict);
        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        var scanStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var savedPlayCounts = new List<int>();
        UserItemData? targetData = null;
        userManager.Setup(manager => manager.GetUserById(sourceId)).Returns(sourceUser);
        userManager.Setup(manager => manager.GetUserById(targetId)).Returns(targetUser);
        libraryManager.Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(() =>
            {
                scanStarted.SetResult();
                releaseScan.Task.GetAwaiter().GetResult();
                return [item];
            });
        userDataManager.Setup(manager => manager.GetUserData(sourceUser, item)).Returns(new UserItemData
        {
            Key = "source",
            Played = true,
            PlayCount = 1,
        });
        userDataManager.Setup(manager => manager.GetUserData(targetUser, item)).Returns(() => targetData);
        userDataManager.Setup(manager => manager.SaveUserData(
                targetUser, item, It.IsAny<UserItemData>(), UserDataSaveReason.Import, It.IsAny<CancellationToken>()))
            .Callback((User _, BaseItem _, UserItemData data, UserDataSaveReason _, CancellationToken _) =>
            {
                targetData = data;
                savedPlayCounts.Add(data.PlayCount);
            });
        var writer = new WatchStateWriter(userDataManager.Object, userManager.Object, NullLogger<WatchStateWriter>.Instance);
        var fullSync = new FullSyncCoordinator(
            userDataManager.Object,
            userManager.Object,
            libraryManager.Object,
            writer,
            queue,
            configurationProvider,
            NullLogger<FullSyncCoordinator>.Instance);
        var incremental = new IncrementalSyncCoordinator(
            libraryManager.Object,
            writer,
            queue,
            configurationProvider,
            NullLogger<IncrementalSyncCoordinator>.Instance);

        Assert.Equal(FullSyncStartOutcome.Accepted, fullSync.Start(sourceId).Outcome);
        var worker = Task.Run(() => queue.ProcessAsync(CancellationToken.None));
        await scanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        incremental.HandleUserDataSaved(new UserDataSaveEventArgs
        {
            UserId = sourceId,
            Item = item,
            UserData = new UserItemData { Key = "source", Played = true, PlayCount = 2 },
            SaveReason = UserDataSaveReason.UpdateUserRating,
            Keys = ["source"],
        });
        queue.Complete();
        releaseScan.SetResult();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], savedPlayCounts);
        Assert.Equal("Completed", fullSync.Status.State);
    }

    private sealed class TestConfigurationProvider(PluginConfiguration configuration) : IPluginConfigurationProvider
    {
        public PluginConfiguration? GetConfiguration() => configuration;
    }
}
