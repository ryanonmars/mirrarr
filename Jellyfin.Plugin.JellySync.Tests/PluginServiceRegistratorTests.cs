using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellySync.Configuration;
using Jellyfin.Plugin.JellySync.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Jellyfin.Plugin.JellySync.Tests;

public class PluginServiceRegistratorTests
{
    [Fact]
    public async Task Parameterless_registrator_registers_one_hosted_service_that_processes_the_coordinator_queue()
    {
        var services = new ServiceCollection();
        var registrator = Assert.IsType<PluginServiceRegistrator>(Activator.CreateInstance(typeof(PluginServiceRegistrator)));
        registrator.RegisterServices(services, Mock.Of<IServerApplicationHost>());

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ISyncWorkQueue) && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WatchStateWriter) && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IncrementalSyncCoordinator) && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(FullSyncCoordinator) && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IFullSyncCoordinator) && descriptor.Lifetime == ServiceLifetime.Singleton);

        var sourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var target = new User("target", "Jellyfin", "Jellyfin") { Id = targetId };
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var userDataManager = new Mock<IUserDataManager>(MockBehavior.Strict);
        var userManager = new Mock<IUserManager>(MockBehavior.Strict);
        userManager.Setup(manager => manager.GetUserById(targetId)).Returns(target);
        userDataManager.Setup(manager => manager.GetUserData(target, It.IsAny<BaseItem>())).Returns((UserItemData?)null);
        userDataManager.Setup(manager => manager.SaveUserData(
                target,
                It.IsAny<BaseItem>(),
                It.IsAny<UserItemData>(),
                UserDataSaveReason.Import,
                It.IsAny<CancellationToken>()))
            .Callback((User _, BaseItem _, UserItemData _, UserDataSaveReason _, CancellationToken _) => saved.SetResult());
        services.AddSingleton<IUserDataManager>(userDataManager.Object);
        services.AddSingleton<IUserManager>(userManager.Object);
        services.AddSingleton<ILibraryManager>(Mock.Of<ILibraryManager>());
        services.AddSingleton<IPluginConfigurationProvider>(new TestConfigurationProvider(new PluginConfiguration
        {
            Enabled = true,
            UserIds = [sourceId, targetId],
        }));
        services.AddLogging();

        await using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<IncrementalSyncCoordinator>();
        var hostedService = Assert.IsType<IncrementalSyncHostedService>(provider.GetServices<IHostedService>().Single());
        await hostedService.StartAsync(CancellationToken.None);
        coordinator.HandleUserDataSaved(new UserDataSaveEventArgs
        {
            UserId = sourceId,
            Item = new TestMovie { Id = Guid.Parse("66666666-6666-6666-6666-666666666666") },
            UserData = new UserItemData
            {
                Key = "source-key",
                Played = true,
                PlayCount = 1,
                LastPlayedDate = new DateTime(2026, 8, 29, 14, 30, 0, DateTimeKind.Utc),
                PlaybackPositionTicks = 123456789,
            },
            SaveReason = UserDataSaveReason.UpdateUserRating,
            Keys = ["source-key"],
        });

        await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None);
    }

    private sealed class TestConfigurationProvider(PluginConfiguration configuration) : IPluginConfigurationProvider
    {
        public PluginConfiguration? GetConfiguration() => configuration;
    }
}
