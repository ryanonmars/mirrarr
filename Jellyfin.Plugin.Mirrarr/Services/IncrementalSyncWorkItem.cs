using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Mirrarr.Services;

public sealed record IncrementalSyncWorkItem(Guid SourceUserId, BaseItem Item, WatchStateSnapshot Snapshot);
