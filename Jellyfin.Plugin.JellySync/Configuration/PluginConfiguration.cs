using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellySync.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; }

    public Guid[] UserIds { get; set; } = [];

    public bool IncludeAllLibraries { get; set; } = true;

    public Guid[] LibraryIds { get; set; } = [];
}
