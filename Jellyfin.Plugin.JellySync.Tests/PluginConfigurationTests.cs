using Jellyfin.Plugin.JellySync.Configuration;

namespace Jellyfin.Plugin.JellySync.Tests;

public class PluginConfigurationTests
{
    [Fact]
    public void New_configuration_uses_safe_sync_defaults()
    {
        var configuration = new PluginConfiguration();

        Assert.False(configuration.Enabled);
        Assert.IsType<Guid[]>(configuration.UserIds);
        Assert.Empty(configuration.UserIds);
        Assert.True(configuration.IncludeAllLibraries);
        Assert.IsType<Guid[]>(configuration.LibraryIds);
        Assert.Empty(configuration.LibraryIds);
    }
}
