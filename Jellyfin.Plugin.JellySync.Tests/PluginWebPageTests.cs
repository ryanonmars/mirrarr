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

    [Fact]
    public void Configuration_page_uses_the_stable_users_and_virtual_folders_endpoints()
    {
        const string resourceName = "Jellyfin.Plugin.JellySync.Configuration.configPage.html";
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var page = reader.ReadToEnd();

        Assert.Contains("ApiClient.getJSON(ApiClient.getUrl('Users'))", page, StringComparison.Ordinal);
        Assert.Contains("ApiClient.getJSON(ApiClient.getUrl('Library/VirtualFolders'))", page, StringComparison.Ordinal);
        Assert.Contains("page.addEventListener('viewshow', loadPage)", page, StringComparison.Ordinal);
        Assert.Contains("page.addEventListener('viewhide', stopPolling)", page, StringComparison.Ordinal);
    }
}
