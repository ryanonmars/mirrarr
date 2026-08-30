using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mirrarr.Services;

public sealed class WatchStateWriter
{
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<WatchStateWriter> _logger;

    public WatchStateWriter(
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILogger<WatchStateWriter> logger)
    {
        _userDataManager = userDataManager;
        _userManager = userManager;
        _logger = logger;
    }

    public WatchStateWriteResult Apply(
        Guid targetUserId,
        BaseItem item,
        WatchStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var targetUser = _userManager.GetUserById(targetUserId);
            if (targetUser is null)
            {
                _logger.LogWarning("Skipping sync target because user {UserId} no longer exists.", targetUserId);
                return WatchStateWriteResult.MissingUser;
            }

            var targetData = _userDataManager.GetUserData(targetUser, item) ?? new UserItemData
            {
                Key = item.GetUserDataKeys().First(),
            };

            if (WatchStateSnapshot.From(targetData) == snapshot)
            {
                return WatchStateWriteResult.Unchanged;
            }

            snapshot.ApplyTo(targetData);
            _userDataManager.SaveUserData(
                targetUser,
                item,
                targetData,
                UserDataSaveReason.Import,
                cancellationToken);
            return WatchStateWriteResult.Updated;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed sync for target user {UserId} and item {ItemId}.", targetUserId, item.Id);
            return WatchStateWriteResult.Failed;
        }
    }
}
