using Jellyfin.Plugin.Mirrarr.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Mirrarr;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IPluginConfigurationProvider, PluginConfigurationProvider>();
        serviceCollection.AddSingleton<ISyncWorkQueue, SyncWorkQueue>();
        serviceCollection.AddSingleton<WatchStateWriter>();
        serviceCollection.AddSingleton(provider => new IncrementalSyncCoordinator(
            provider.GetRequiredService<ILibraryManager>(),
            provider.GetRequiredService<WatchStateWriter>(),
            provider.GetRequiredService<ISyncWorkQueue>(),
            provider.GetRequiredService<IPluginConfigurationProvider>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<IncrementalSyncCoordinator>>()));
        serviceCollection.AddSingleton<FullSyncCoordinator>();
        serviceCollection.AddSingleton<IFullSyncCoordinator>(provider => provider.GetRequiredService<FullSyncCoordinator>());
        serviceCollection.AddHostedService<IncrementalSyncHostedService>();
    }
}
