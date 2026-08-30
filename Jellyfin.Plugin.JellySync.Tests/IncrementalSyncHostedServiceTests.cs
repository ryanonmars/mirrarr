using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellySync.Configuration;
using Jellyfin.Plugin.JellySync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.JellySync.Tests;

public class IncrementalSyncHostedServiceTests
{
    [Fact]
    public async Task Callback_returns_while_the_worker_save_is_blocked()
    {
        var harness = new HostedHarness();
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSaveToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnSave = (_, _) =>
        {
            saveStarted.SetResult();
            allowSaveToFinish.Task.GetAwaiter().GetResult();
        };

        await harness.Service.StartAsync(CancellationToken.None);
        var callbackTask = Task.Run(() => harness.Raise(UserA, WatchedData(1)));

        await callbackTask.WaitAsync(TimeSpan.FromSeconds(5));
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(allowSaveToFinish.Task.IsCompleted);

        allowSaveToFinish.SetResult();
        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stop_unsubscribes_then_drains_every_queued_item_before_returning()
    {
        var harness = new HostedHarness();
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstSaveToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnSave = (data, _) =>
        {
            if (data.PlayCount == 1)
            {
                firstSaveStarted.SetResult();
                allowFirstSaveToFinish.Task.GetAwaiter().GetResult();
            }
        };

        await harness.Service.StartAsync(CancellationToken.None);
        harness.Raise(UserA, WatchedData(1));
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Raise(UserA, WatchedData(2));

        var stopTask = harness.Service.StopAsync(CancellationToken.None);
        Assert.False(stopTask.IsCompleted);
        allowFirstSaveToFinish.SetResult();
        await stopTask;

        Assert.Equal([1, 2], harness.SavedPlayCounts);
        harness.UserDataManager.VerifyRemove(manager => manager.UserDataSaved -= It.IsAny<EventHandler<UserDataSaveEventArgs>>(), Times.Once);
    }

    [Fact]
    public async Task Stop_deadline_cancels_the_worker_and_leaves_later_queued_work_unprocessed()
    {
        var harness = new HostedHarness();
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnSave = (data, cancellationToken) =>
        {
            if (data.PlayCount == 1)
            {
                firstSaveStarted.SetResult();
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
            }
        };

        await harness.Service.StartAsync(CancellationToken.None);
        harness.Raise(UserA, WatchedData(1));
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Raise(UserA, WatchedData(2));
        using var stopDeadline = new CancellationTokenSource();

        var stopTask = harness.Service.StopAsync(stopDeadline.Token);
        stopDeadline.Cancel();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1], harness.SavedPlayCounts);
    }

    [Fact]
    public async Task Stop_deadline_returns_while_an_uncooperative_save_is_still_blocked()
    {
        var harness = new HostedHarness();
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstSaveToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnSave = (data, _) =>
        {
            if (data.PlayCount == 1)
            {
                firstSaveStarted.SetResult();
                allowFirstSaveToFinish.Task.GetAwaiter().GetResult();
            }
            else
            {
                secondSaveStarted.SetResult();
            }
        };

        await harness.Service.StartAsync(CancellationToken.None);
        harness.Raise(UserA, WatchedData(1));
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Raise(UserA, WatchedData(2));
        using var stopDeadline = new CancellationTokenSource();

        var stopTask = harness.Service.StopAsync(stopDeadline.Token);
        stopDeadline.Cancel();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([1], harness.SavedPlayCounts);

        allowFirstSaveToFinish.SetResult();
        var laterWork = await Task.WhenAny(secondSaveStarted.Task, Task.Delay(TimeSpan.FromMilliseconds(100)));
        Assert.NotSame(secondSaveStarted.Task, laterWork);
        Assert.Equal([1], harness.SavedPlayCounts);
    }

    private static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static UserItemData WatchedData(int playCount) => new()
    {
        Key = "source-key",
        Played = true,
        PlayCount = playCount,
        LastPlayedDate = new DateTime(2026, 8, 29, 14, 30, 0, DateTimeKind.Utc),
        PlaybackPositionTicks = 123456789,
    };

    private sealed class HostedHarness
    {
        public HostedHarness()
        {
            UserDataManager = new Mock<IUserDataManager>(MockBehavior.Strict);
            var userManager = new Mock<IUserManager>(MockBehavior.Strict);
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            var targetUser = new User("B", "Jellyfin", "Jellyfin") { Id = UserB };
            userManager.Setup(manager => manager.GetUserById(UserB)).Returns(targetUser);
            UserDataManager.Setup(manager => manager.GetUserData(targetUser, It.IsAny<BaseItem>())).Returns((UserItemData?)null);
            UserDataManager.Setup(manager => manager.SaveUserData(
                    targetUser,
                    It.IsAny<BaseItem>(),
                    It.IsAny<UserItemData>(),
                    UserDataSaveReason.Import,
                    It.IsAny<CancellationToken>()))
                .Callback((User _, BaseItem _, UserItemData data, UserDataSaveReason _, CancellationToken cancellationToken) =>
                {
                    SavedPlayCounts.Add(data.PlayCount);
                    OnSave?.Invoke(data, cancellationToken);
                });
            var configurationProvider = new TestConfigurationProvider(new PluginConfiguration
            {
                Enabled = true,
                UserIds = [UserA, UserB],
            });
            var coordinator = new IncrementalSyncCoordinator(
                UserDataManager.Object,
                userManager.Object,
                libraryManager.Object,
                new SyncWorkQueue(),
                configurationProvider,
                NullLogger<IncrementalSyncCoordinator>.Instance);
            Service = new IncrementalSyncHostedService(
                UserDataManager.Object,
                coordinator,
                NullLogger<IncrementalSyncHostedService>.Instance);
        }

        public Mock<IUserDataManager> UserDataManager { get; }

        public IncrementalSyncHostedService Service { get; }

        public List<int> SavedPlayCounts { get; } = [];

        public Action<UserItemData, CancellationToken>? OnSave { get; set; }

        public void Raise(Guid userId, UserItemData userData) => UserDataManager.Raise(
            manager => manager.UserDataSaved += null!,
            UserDataManager.Object,
            new UserDataSaveEventArgs
            {
                UserId = userId,
                Item = new TestMovie { Id = Guid.Parse("66666666-6666-6666-6666-666666666666") },
                UserData = userData,
                SaveReason = UserDataSaveReason.UpdateUserRating,
                Keys = [userData.Key],
            });
    }

    private sealed class TestConfigurationProvider(PluginConfiguration configuration) : IPluginConfigurationProvider
    {
        public PluginConfiguration? GetConfiguration() => configuration;
    }
}
