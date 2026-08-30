using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.JellySync.Tests;

internal sealed class TestMovie : Movie
{
    public override SourceType SourceType => SourceType.Library;
}

internal sealed class TestEpisode : Episode
{
    public override SourceType SourceType => SourceType.Library;
}
