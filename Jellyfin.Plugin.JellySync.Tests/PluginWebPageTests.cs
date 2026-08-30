using System.Runtime.CompilerServices;

namespace Jellyfin.Plugin.JellySync.Tests;

public class PluginWebPageTests
{
    [Fact]
    public void Plugin_registers_an_existing_embedded_configuration_page()
    {
        Assert.Contains(typeof(Plugin).GetInterfaces(), type => type.Name == "IHasWebPages");
        var plugin = Assert.IsType<Plugin>(RuntimeHelpers.GetUninitializedObject(typeof(Plugin)));

        var page = Assert.Single(plugin.GetPages());

        Assert.Equal("JellySync", page.DisplayName);
        Assert.Equal("Jellyfin.Plugin.JellySync.Configuration.configPage.html", page.EmbeddedResourcePath);
        Assert.Contains(page.EmbeddedResourcePath, typeof(Plugin).Assembly.GetManifestResourceNames());
    }
}
