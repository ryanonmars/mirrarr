using Jellyfin.Plugin.Mirrarr.Configuration;

namespace Jellyfin.Plugin.Mirrarr.Services;

public interface IPluginConfigurationProvider
{
    PluginConfiguration? GetConfiguration();
}
