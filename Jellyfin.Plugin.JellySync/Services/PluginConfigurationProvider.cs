using Jellyfin.Plugin.JellySync.Configuration;

namespace Jellyfin.Plugin.JellySync.Services;

public sealed class PluginConfigurationProvider : IPluginConfigurationProvider
{
    public PluginConfiguration? GetConfiguration() => Plugin.Instance?.Configuration;
}
