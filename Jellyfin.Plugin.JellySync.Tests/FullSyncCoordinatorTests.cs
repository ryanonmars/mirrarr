using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellySync.Configuration;
using Jellyfin.Plugin.JellySync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.JellySync.Tests;

public class FullSyncCoordinatorTests
{
    private static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserC = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid LibraryA = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Rejects_invalid_configuration_source_and_an_active_job()
    {
        var disabled = new Harness(new PluginConfiguration());
        Assert.Equal(FullSyncStartOutcome.InvalidConfiguration, disabled.Coordinator.Start(UserA).Outcome);

        var harness = new Harness(ValidConfiguration());
        Assert.Equal(FullSyncStartOutcome.InvalidRequest, harness.Coordinator.Start(Guid.Empty).Outcome);
        Assert.Equal(FullSyncStartOutcome.InvalidSource, harness.Coordinator.Start(UserC).Outcome);
        Assert.Equal(FullSyncStartOutcome.Accepted, harness.Coordinator.Start(UserA).Outcome);
        Assert.Equal(FullSyncStartOutcome.AlreadyActive, harness.Coordinator.Start(UserB).Outcome);
        Assert.Equal("Queued", harness.Coordinator.Status.State);
    }

    [Fact]
    public void Queue_failure_restores_idle_status_and_allows_a_retry()
    {
        var queue = new CapturingQueue { Accept = false };
        var harness = new Harness(ValidConfiguration(), queue);

        Assert.Equal(FullSyncStartOutcome.WorkerUnavailable, harness.Coordinator.Start(UserA).Outcome);
        Assert.Equal("Idle", harness.Coordinator.Status.State);
        queue.Accept = true;
        Assert.Equal(FullSyncStartOutcome.Accepted, harness.Coordinator.Start(UserA).Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Queries_recursive_movies_and_episodes_in_the_configured_library_scope(bool allLibraries)
    {
        var configuration = ValidConfiguration();
        configuration.IncludeAllLibraries = allLibraries;
        configuration.LibraryIds = allLibraries ? [] : [LibraryA, Guid.Empty, LibraryA];
        var harness = new Harness(configuration);
        harness.Items.Add(new TestMovie { Id = Guid.NewGuid() });

        Assert.Equal(FullSyncStartOutcome.Accepted, harness.Coordinator.Start(UserA).Outcome);
        await harness.RunQueuedAsync();

        Assert.NotNull(harness.Query);
        Assert.True(harness.Query.Recursive);
        Assert.Equal([BaseItemKind.Movie, BaseItemKind.Episode], harness.Query.IncludeItemTypes);
        Assert.Equal(allLibraries ? [] : [LibraryA], harness.Query.TopParentIds);
    }

    [Fact]
    public async Task Null_source_data_clears_every_other_user_and_reports_complete_counters()
    {
        var harness = new Harness(ValidConfiguration(UserA, UserB, UserC));
        var movie = new TestMovie { Id = Guid.NewGuid() };
        var episode = new TestEpisode { Id = Guid.NewGuid() };
        harness.Items.AddRange([movie, episode]);
        harness.SetTarget(UserB, movie, new UserItemData { Key = "b", Played = true, PlayCount = 2, PlaybackPositionTicks = 50, IsFavorite = true });
        harness.SetTarget(UserC, movie, new UserItemData { Key = "c", Played = true, PlayCount = 3, PlaybackPositionTicks = 60 });
        harness.SetTarget(UserB, episode, new UserItemData { Key = "b-episode", Played = true, PlayCount = 1 });
        harness.SetTarget(UserC, episode, new UserItemData { Key = "c-episode", Played = true, PlayCount = 1 });

        harness.Coordinator.Start(UserA);
        await harness.RunQueuedAsync();

        Assert.Equal(4, harness.Saves.Count);
        Assert.All(harness.Saves, save =>
        {
            Assert.False(save.Data.Played);
            Assert.Equal(0, save.Data.PlayCount);
            Assert.Null(save.Data.LastPlayedDate);
            Assert.Equal(0, save.Data.PlaybackPositionTicks);
        });
        Assert.True(harness.Saves.Single(save => save.User.Id == UserB && save.Item == movie).Data.IsFavorite);
        var status = harness.Coordinator.Status;
        Assert.Equal("Completed", status.State);
        Assert.Equal(2, status.TotalItems);
        Assert.Equal(2, status.ProcessedItems);
        Assert.Equal(4, status.UpdatedWrites);
        Assert.Equal(0, status.UnchangedWrites);
        Assert.Equal(0, status.FailedWrites);
        Assert.NotNull(status.StartedUtc);
        Assert.NotNull(status.CompletedUtc);
    }

    [Fact]
    public async Task Continues_after_target_failures_and_counts_updated_unchanged_and_failed_writes()
    {
        var harness = new Harness(ValidConfiguration(UserA, UserB, UserC));
        var item = new TestMovie { Id = Guid.NewGuid() };
        harness.Items.Add(item);
        harness.SetSource(item, new UserItemData { Key = "source", Played = true, PlayCount = 1 });
        harness.SetTarget(UserB, item, new UserItemData { Key = "same", Played = true, PlayCount = 1 });
        harness.UserManager.Setup(manager => manager.GetUserById(UserC)).Returns((User?)null);

        harness.Coordinator.Start(UserA);
        await harness.RunQueuedAsync();

        var status = harness.Coordinator.Status;
        Assert.Equal("Completed", status.State);
        Assert.Equal(0, status.UpdatedWrites);
        Assert.Equal(1, status.UnchangedWrites);
        Assert.Equal(1, status.FailedWrites);
    }

    [Fact]
    public async Task Source_state_overwrites_every_other_snapshotted_user()
    {
        var harness = new Harness(ValidConfiguration(UserA, UserB, UserC));
        var item = new TestMovie { Id = Guid.NewGuid() };
        var lastPlayed = new DateTime(2026, 8, 29, 20, 30, 0, DateTimeKind.Utc);
        harness.Items.Add(item);
        harness.SetSource(item, new UserItemData
        {
            Key = "source",
            Played = true,
            PlayCount = 7,
            LastPlayedDate = lastPlayed,
            PlaybackPositionTicks = 9876,
        });

        harness.Coordinator.Start(UserA);
        await harness.RunQueuedAsync();

        Assert.Equal([UserB, UserC], harness.Saves.Select(save => save.User.Id));
        Assert.All(harness.Saves, save =>
        {
            Assert.True(save.Data.Played);
            Assert.Equal(7, save.Data.PlayCount);
            Assert.Equal(lastPlayed, save.Data.LastPlayedDate);
            Assert.Equal(9876, save.Data.PlaybackPositionTicks);
        });
        Assert.Equal(2, harness.Coordinator.Status.UpdatedWrites);
    }

    [Fact]
    public async Task Fatal_scan_failure_sets_failed_status_and_error()
    {
        var harness = new Harness(ValidConfiguration());
        harness.LibraryManager.Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Throws(new InvalidOperationException("scan unavailable"));

        harness.Coordinator.Start(UserA);
        await harness.RunQueuedAsync();

        Assert.Equal("Failed", harness.Coordinator.Status.State);
        Assert.Contains("scan unavailable", harness.Coordinator.Status.LatestError, StringComparison.Ordinal);
        Assert.NotNull(harness.Coordinator.Status.CompletedUtc);
    }

    private static PluginConfiguration ValidConfiguration(params Guid[] users) => new()
    {
        Enabled = true,
        UserIds = users.Length == 0 ? [UserA, UserB] : users,
        IncludeAllLibraries = true,
    };

    private sealed class Harness
    {
        private readonly Dictionary<(Guid UserId, Guid ItemId), UserItemData?> _data = [];
        private readonly CapturingQueue _queue;

        public Harness(PluginConfiguration configuration, CapturingQueue? queue = null)
        {
            _queue = queue ?? new CapturingQueue();
            UserDataManager = new Mock<IUserDataManager>(MockBehavior.Strict);
            UserManager = new Mock<IUserManager>(MockBehavior.Strict);
            LibraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            foreach (var id in new[] { UserA, UserB, UserC })
            {
                var user = new User(id.ToString(), "Jellyfin", "Jellyfin") { Id = id };
                UserManager.Setup(manager => manager.GetUserById(id)).Returns(user);
            }

            LibraryManager.Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery query) =>
                {
                    Query = query;
                    return Items;
                });
            UserDataManager.Setup(manager => manager.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>()))
                .Returns((User user, BaseItem item) => _data.TryGetValue((user.Id, item.Id), out var data) ? data : null);
            UserDataManager.Setup(manager => manager.SaveUserData(
                    It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), UserDataSaveReason.Import, It.IsAny<CancellationToken>()))
                .Callback((User user, BaseItem item, UserItemData data, UserDataSaveReason _, CancellationToken _) =>
                {
                    _data[(user.Id, item.Id)] = data;
                    Saves.Add(new SavedData(user, item, data));
                });
            var writer = new WatchStateWriter(UserDataManager.Object, UserManager.Object, NullLogger<WatchStateWriter>.Instance);
            Coordinator = new FullSyncCoordinator(
                UserDataManager.Object,
                UserManager.Object,
                LibraryManager.Object,
                writer,
                _queue,
                new TestConfigurationProvider(configuration),
                NullLogger<FullSyncCoordinator>.Instance);
        }

        public FullSyncCoordinator Coordinator { get; }
        public Mock<IUserDataManager> UserDataManager { get; }
        public Mock<IUserManager> UserManager { get; }
        public Mock<ILibraryManager> LibraryManager { get; }
        public List<BaseItem> Items { get; } = [];
        public List<SavedData> Saves { get; } = [];
        public InternalItemsQuery? Query { get; private set; }

        public void SetSource(BaseItem item, UserItemData data) => SetTarget(UserA, item, data);
        public void SetTarget(Guid userId, BaseItem item, UserItemData data) => _data[(userId, item.Id)] = data;
        public Task RunQueuedAsync() => _queue.Enqueued!.ProcessAsync(CancellationToken.None);
    }

    private sealed class TestConfigurationProvider(PluginConfiguration configuration) : IPluginConfigurationProvider
    {
        public PluginConfiguration? GetConfiguration() => configuration;
    }

    private sealed class CapturingQueue : ISyncWorkQueue
    {
        public bool Accept { get; set; } = true;
        public ISyncWorkItem? Enqueued { get; private set; }
        public bool TryEnqueue(ISyncWorkItem workItem)
        {
            if (Accept)
            {
                Enqueued = workItem;
            }

            return Accept;
        }

        public Task ProcessAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Complete() { }
    }

    public sealed record SavedData(User User, BaseItem Item, UserItemData Data);
}
