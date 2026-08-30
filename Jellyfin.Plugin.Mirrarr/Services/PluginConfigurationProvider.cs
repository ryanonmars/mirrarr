using Jellyfin.Plugin.Mirrarr.Configuration;

namespace Jellyfin.Plugin.Mirrarr.Services;

public sealed class PluginConfigurationProvider : IPluginConfigurationProvider
{
    public PluginConfiguration? GetConfiguration() => Plugin.Instance?.Configuration;
}
