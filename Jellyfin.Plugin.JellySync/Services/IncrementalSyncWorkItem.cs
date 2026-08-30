using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.JellySync.Services;

public sealed record IncrementalSyncWorkItem(Guid SourceUserId, BaseItem Item, WatchStateSnapshot Snapshot);
