using Jellyfin.Plugin.JellySync.Configuration;

namespace Jellyfin.Plugin.JellySync.Services;

public interface IPluginConfigurationProvider
{
    PluginConfiguration? GetConfiguration();
}
