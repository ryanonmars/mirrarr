using System.Runtime.CompilerServices;

namespace Jellyfin.Plugin.JellySync.Tests;

public class PluginWebPageTests
{
    [Fact]
    public void Plugin_registers_an_existing_embedded_configuration_page()
    {
        Assert.Contains(typeof(Plugin).GetInterfaces(), type => type.Name == "IHasWebPages");
        var plugin = Assert.IsType<Plugin>(RuntimeHelpers.GetUninitializedObject(typeof(Plugin)));

        var pages = plugin.GetPages().ToArray();
        var page = Assert.Single(pages, page => page.Name == "JellySync");

        Assert.Equal("JellySync", page.DisplayName);
        Assert.Equal("Jellyfin.Plugin.JellySync.Configuration.configPage.html", page.EmbeddedResourcePath);
        Assert.Contains(page.EmbeddedResourcePath, typeof(Plugin).Assembly.GetManifestResourceNames());
        var script = Assert.Single(pages, page => page.Name == "jellysync-v0.1.12.js");
        Assert.Equal("Jellyfin.Plugin.JellySync.Configuration.configPage.js", script.EmbeddedResourcePath);
        Assert.Contains(script.EmbeddedResourcePath, typeof(Plugin).Assembly.GetManifestResourceNames());
    }

    [Fact]
    public void Configuration_page_loads_its_script_as_a_jellyfin_embedded_resource()
    {
        const string resourceName = "Jellyfin.Plugin.JellySync.Configuration.configPage.html";
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var page = reader.ReadToEnd();

        Assert.Contains("configurationpage?name=jellysync-v0.1.12.js", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_script_uses_the_stable_users_and_virtual_folders_endpoints()
    {
        const string resourceName = "Jellyfin.Plugin.JellySync.Configuration.configPage.js";
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var page = reader.ReadToEnd();

        Assert.Contains("ApiClient.getJSON(ApiClient.getUrl('Users'))", page, StringComparison.Ordinal);
        Assert.Contains("ApiClient.getJSON(ApiClient.getUrl('Library/VirtualFolders'))", page, StringComparison.Ordinal);
        Assert.Contains("page.addEventListener('viewshow', loadPage)", page, StringComparison.Ordinal);
        Assert.Contains("page.addEventListener('viewhide', stopPolling)", page, StringComparison.Ordinal);
        Assert.Contains("page.addEventListener('viewhide', stopPolling);\n        loadPage();", page, StringComparison.Ordinal);
        Assert.Contains("new MutationObserver(initializePage)", page, StringComparison.Ordinal);
        Assert.Contains("isSupportedLibrary", page, StringComparison.Ordinal);
        Assert.Contains("ensureStyles", page, StringComparison.Ordinal);
        Assert.Contains("status.State ?? status.state", page, StringComparison.Ordinal);
    }
}
