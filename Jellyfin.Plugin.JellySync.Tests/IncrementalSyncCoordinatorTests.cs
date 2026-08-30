using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellySync.Configuration;
using Jellyfin.Plugin.JellySync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Jellyfin.Plugin.JellySync.Tests;

public class IncrementalSyncCoordinatorTests
{
    private static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserC = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid LibraryA = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid LibraryB = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ItemId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Theory]
    [MemberData(nameof(RejectedConfigurations))]
    public async Task Rejects_disabled_or_invalid_configuration(PluginConfiguration configuration)
    {
        var harness = new SyncHarness(configuration);

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));
        await harness.DrainAsync();

        Assert.Empty(harness.Saves);
    }

    public static IEnumerable<object[]> RejectedConfigurations =>
    [
        [new PluginConfiguration { Enabled = false, UserIds = [UserA, UserB] }],
        [new PluginConfiguration { Enabled = true, UserIds = [UserA] }],
    ];

    [Fact]
    public async Task Rejects_import_saves()
    {
        var harness = new SyncHarness(ValidConfiguration());
        var args = harness.EventFor(UserA, MovieItem(), WatchedData());
        args.SaveReason = UserDataSaveReason.Import;

        harness.Coordinator.HandleUserDataSaved(args);
        await harness.DrainAsync();

        Assert.Empty(harness.Saves);
    }

    [Fact]
    public async Task Rejects_events_from_users_outside_the_configuration()
    {
        var harness = new SyncHarness(ValidConfiguration());

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserC, MovieItem(), WatchedData()));
        await harness.DrainAsync();

        Assert.Empty(harness.Saves);
    }

    [Fact]
    public async Task Rejects_items_other_than_movies_or_episodes()
    {
        var harness = new SyncHarness(ValidConfiguration());

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, new Folder { Id = ItemId }, WatchedData()));
        await harness.DrainAsync();

        Assert.Empty(harness.Saves);
    }

    [Fact]
    public async Task Rejects_items_outside_selected_libraries()
    {
        var harness = new SyncHarness(ValidConfiguration(includeAllLibraries: false));
        harness.LibraryManager.Setup(manager => manager.GetCollectionFolders(It.IsAny<BaseItem>()))
            .Returns([new Folder { Id = LibraryB }]);

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));
        await harness.DrainAsync();

        Assert.Empty(harness.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepts_movies_and_episodes_from_every_library_when_all_library_mode_is_enabled(bool isEpisode)
    {
        var harness = new SyncHarness(ValidConfiguration(includeAllLibraries: true));
        BaseItem item = isEpisode ? EpisodeItem() : MovieItem();

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, item, WatchedData()));
        await harness.DrainAsync();

        Assert.Equal(ItemId.ToString(), Assert.Single(harness.Saves).Data.Key);
    }

    [Fact]
    public async Task Accepts_movies_when_any_collection_folder_is_selected()
    {
        var harness = new SyncHarness(ValidConfiguration(includeAllLibraries: false));
        harness.LibraryManager.Setup(manager => manager.GetCollectionFolders(It.IsAny<BaseItem>()))
            .Returns([new Folder { Id = LibraryB }, new Folder { Id = LibraryA }]);

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));
        await harness.DrainAsync();

        Assert.Single(harness.Saves);
    }

    [Fact]
    public async Task Captures_an_immutable_watch_snapshot_before_the_event_data_can_mutate()
    {
        var harness = new SyncHarness(ValidConfiguration());
        var source = WatchedData();

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), source));
        source.Played = false;
        source.PlayCount = 99;
        source.LastPlayedDate = null;
        source.PlaybackPositionTicks = 1;
        await harness.DrainAsync();

        var saved = Assert.Single(harness.Saves).Data;
        Assert.True(saved.Played);
        Assert.Equal(3, saved.PlayCount);
        Assert.Equal(new DateTime(2026, 8, 29, 14, 30, 0, DateTimeKind.Utc), saved.LastPlayedDate);
        Assert.Equal(123456789, saved.PlaybackPositionTicks);
    }

    [Fact]
    public async Task Fans_out_changes_in_both_directions_between_configured_users()
    {
        var harness = new SyncHarness(ValidConfiguration());
        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));
        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserB, MovieItem(), WatchedData(played: false, playCount: 4)));

        await harness.DrainAsync();

        Assert.Equal([UserB, UserA], harness.Saves.Select(save => save.User.Id));
    }

    [Fact]
    public async Task Skips_missing_targets_and_continues_to_other_targets()
    {
        var harness = new SyncHarness(ValidConfiguration(UserA, UserB, UserC));
        harness.UserManager.Setup(manager => manager.GetUserById(UserB)).Returns((User?)null);
        harness.AddUser(UserC);

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));
        await harness.DrainAsync();

        Assert.Equal(UserC, Assert.Single(harness.Saves).User.Id);
    }

    [Fact]
    public async Task Continues_fan_out_after_a_target_failure()
    {
        var harness = new SyncHarness(ValidConfiguration(UserA, UserB, UserC));
        harness.AddUser(UserC);
        harness.UserDataManager.Setup(manager => manager.GetUserData(harness.UserB, It.IsAny<BaseItem>()))
            .Throws(new InvalidOperationException("database unavailable"));

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));
        await harness.DrainAsync();

        Assert.Equal(UserC, Assert.Single(harness.Saves).User.Id);
    }

    [Fact]
    public async Task Copies_exactly_the_four_watch_fields_and_preserves_existing_target_fields()
    {
        var harness = new SyncHarness(ValidConfiguration());
        var target = new UserItemData
        {
            Key = "target-key",
            Played = false,
            PlayCount = 1,
            LastPlayedDate = null,
            PlaybackPositionTicks = 7,
            IsFavorite = true,
            Rating = 9.5,
            AudioStreamIndex = 2,
            SubtitleStreamIndex = 4,
        };
        harness.SetTargetData(harness.UserB, target);

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));
        await harness.DrainAsync();

        var saved = Assert.Single(harness.Saves).Data;
        Assert.True(saved.Played);
        Assert.Equal(3, saved.PlayCount);
        Assert.Equal(new DateTime(2026, 8, 29, 14, 30, 0, DateTimeKind.Utc), saved.LastPlayedDate);
        Assert.Equal(123456789, saved.PlaybackPositionTicks);
        Assert.True(saved.IsFavorite);
        Assert.Equal(9.5, saved.Rating);
        Assert.True(saved.Likes.GetValueOrDefault());
        Assert.Equal(2, saved.AudioStreamIndex);
        Assert.Equal(4, saved.SubtitleStreamIndex);
        Assert.Equal("target-key", saved.Key);
    }

    [Fact]
    public async Task Does_not_save_when_target_already_matches_the_watch_snapshot()
    {
        var harness = new SyncHarness(ValidConfiguration());
        harness.SetTargetData(harness.UserB, WatchedData(key: "target-key"));

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));
        await harness.DrainAsync();

        Assert.Empty(harness.Saves);
    }

    [Fact]
    public async Task Applies_queued_events_in_fifo_order_so_the_last_event_wins()
    {
        var harness = new SyncHarness(ValidConfiguration());
        var source = WatchedData(played: false, playCount: 1, playbackPositionTicks: 10);
        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), source));
        source.Played = true;
        source.PlayCount = 2;
        source.LastPlayedDate = new DateTime(2026, 8, 30, 15, 0, 0, DateTimeKind.Utc);
        source.PlaybackPositionTicks = 20;
        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), source));

        await harness.DrainAsync();

        Assert.Equal(2, harness.Saves.Count);
        var final = harness.Saves[^1].Data;
        Assert.True(final.Played);
        Assert.Equal(2, final.PlayCount);
        Assert.Equal(new DateTime(2026, 8, 30, 15, 0, 0, DateTimeKind.Utc), final.LastPlayedDate);
        Assert.Equal(20, final.PlaybackPositionTicks);
    }

    [Fact]
    public async Task Evaluates_the_latest_configuration_for_each_event()
    {
        var harness = new SyncHarness(new PluginConfiguration { Enabled = false });
        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));
        harness.ConfigurationProvider.Configuration = ValidConfiguration();
        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));

        await harness.DrainAsync();

        Assert.Single(harness.Saves);
    }

    [Fact]
    public async Task Rethrows_worker_cancellation_instead_of_swallowing_it_as_a_target_failure()
    {
        var queue = new CapturingWorkQueue();
        var harness = new SyncHarness(ValidConfiguration(), queue);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        harness.UserDataManager.Setup(manager => manager.SaveUserData(
                harness.UserB,
                It.IsAny<BaseItem>(),
                It.IsAny<UserItemData>(),
                UserDataSaveReason.Import,
                cancellation.Token))
            .Throws(new OperationCanceledException(cancellation.Token));

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.Enqueued!.ProcessAsync(cancellation.Token));
    }

    [Fact]
    public async Task Cancellation_before_the_next_target_prevents_that_target_from_starting()
    {
        var queue = new CapturingWorkQueue();
        var harness = new SyncHarness(ValidConfiguration(UserA, UserB, UserC), queue);
        harness.AddUser(UserC);
        using var cancellation = new CancellationTokenSource();
        harness.UserDataManager.Setup(manager => manager.SaveUserData(
                harness.UserB,
                It.IsAny<BaseItem>(),
                It.IsAny<UserItemData>(),
                UserDataSaveReason.Import,
                cancellation.Token))
            .Callback((User _, BaseItem _, UserItemData _, UserDataSaveReason _, CancellationToken _) => cancellation.Cancel());

        harness.Coordinator.HandleUserDataSaved(harness.EventFor(UserA, MovieItem(), WatchedData()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.Enqueued!.ProcessAsync(cancellation.Token));
        Assert.Empty(harness.Saves);
    }

    private static PluginConfiguration ValidConfiguration(bool includeAllLibraries = true) =>
        ValidConfiguration(UserA, UserB, includeAllLibraries);

    private static PluginConfiguration ValidConfiguration(Guid firstUser, Guid secondUser, bool includeAllLibraries = true) =>
        ValidConfiguration([firstUser, secondUser], includeAllLibraries);

    private static PluginConfiguration ValidConfiguration(Guid firstUser, Guid secondUser, Guid thirdUser) =>
        ValidConfiguration([firstUser, secondUser, thirdUser], includeAllLibraries: true);

    private static PluginConfiguration ValidConfiguration(Guid[] userIds, bool includeAllLibraries) => new()
    {
        Enabled = true,
        UserIds = userIds,
        IncludeAllLibraries = includeAllLibraries,
        LibraryIds = includeAllLibraries ? [] : [LibraryA],
    };

    private static Movie MovieItem() => new TestMovie { Id = ItemId };

    private static Episode EpisodeItem() => new TestEpisode { Id = ItemId };

    private static UserItemData WatchedData(
        bool played = true,
        int playCount = 3,
        long playbackPositionTicks = 123456789,
        string key = "source-key") => new()
        {
            Key = key,
            Played = played,
            PlayCount = playCount,
            LastPlayedDate = new DateTime(2026, 8, 29, 14, 30, 0, DateTimeKind.Utc),
            PlaybackPositionTicks = playbackPositionTicks,
        };

    private sealed class SyncHarness
    {
        private readonly Dictionary<Guid, UserItemData?> _targetData = [];

        public SyncHarness(PluginConfiguration configuration, ISyncWorkQueue? workQueue = null)
        {
            ConfigurationProvider = new TestConfigurationProvider(configuration);
            UserDataManager = new Mock<IUserDataManager>(MockBehavior.Strict);
            UserManager = new Mock<IUserManager>(MockBehavior.Strict);
            LibraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            UserA = CreateUser(IncrementalSyncCoordinatorTests.UserA, "A");
            UserB = CreateUser(IncrementalSyncCoordinatorTests.UserB, "B");
            AddUser(IncrementalSyncCoordinatorTests.UserA, UserA);
            AddUser(IncrementalSyncCoordinatorTests.UserB, UserB);
            LibraryManager.Setup(manager => manager.GetCollectionFolders(It.IsAny<BaseItem>())).Returns([]);
            UserDataManager.Setup(manager => manager.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>()))
                .Returns((User user, BaseItem _) => _targetData.TryGetValue(user.Id, out var data) ? data : null);
            UserDataManager.Setup(manager => manager.SaveUserData(
                    It.IsAny<User>(),
                    It.IsAny<BaseItem>(),
                    It.IsAny<UserItemData>(),
                    UserDataSaveReason.Import,
                    It.IsAny<CancellationToken>()))
                .Callback((User user, BaseItem _, UserItemData data, UserDataSaveReason _, CancellationToken _) =>
                    Saves.Add(new SavedUserData(user, data)));
            Coordinator = new IncrementalSyncCoordinator(
                UserDataManager.Object,
                UserManager.Object,
                LibraryManager.Object,
                workQueue ?? new SyncWorkQueue(),
                ConfigurationProvider,
                Logger.Object);
        }

        public TestConfigurationProvider ConfigurationProvider { get; }

        public Mock<IUserDataManager> UserDataManager { get; }

        public Mock<IUserManager> UserManager { get; }

        public Mock<ILibraryManager> LibraryManager { get; }

        public IncrementalSyncCoordinator Coordinator { get; }

        public User UserA { get; }

        public User UserB { get; }

        public List<SavedUserData> Saves { get; } = [];

        public Mock<ILogger<IncrementalSyncCoordinator>> Logger { get; } = new();

        public void AddUser(Guid id)
        {
            AddUser(id, CreateUser(id, id.ToString()));
        }

        public void SetTargetData(User user, UserItemData data)
        {
            _targetData[user.Id] = data;
        }

        public UserDataSaveEventArgs EventFor(Guid userId, BaseItem item, UserItemData data) => new()
        {
            UserId = userId,
            Item = item,
            UserData = data,
            SaveReason = UserDataSaveReason.UpdateUserRating,
            Keys = [data.Key],
        };

        public async Task DrainAsync()
        {
            Coordinator.Complete();
            await Coordinator.ProcessQueueAsync(CancellationToken.None);
        }

        private void AddUser(Guid id, User user)
        {
            UserManager.Setup(manager => manager.GetUserById(id)).Returns(user);
        }

        private static User CreateUser(Guid id, string name) => new(name, "Jellyfin", "Jellyfin") { Id = id };
    }

    private sealed class TestConfigurationProvider(PluginConfiguration configuration) : IPluginConfigurationProvider
    {
        public PluginConfiguration Configuration { get; set; } = configuration;

        public PluginConfiguration? GetConfiguration() => Configuration;
    }

    private sealed record SavedUserData(User User, UserItemData Data);

    private sealed class CapturingWorkQueue : ISyncWorkQueue
    {
        public ISyncWorkItem? Enqueued { get; private set; }

        public bool TryEnqueue(ISyncWorkItem workItem)
        {
            Enqueued = workItem;
            return true;
        }

        public Task ProcessAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Complete()
        {
        }
    }
}
