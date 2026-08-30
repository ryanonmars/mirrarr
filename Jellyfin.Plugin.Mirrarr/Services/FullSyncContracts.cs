namespace Jellyfin.Plugin.Mirrarr.Services;

public sealed record FullSyncRequest(Guid SourceUserId);

public sealed record FullSyncStatus(
    string State,
    Guid? SourceUserId,
    int TotalItems,
    int ProcessedItems,
    int UpdatedWrites,
    int UnchangedWrites,
    int FailedWrites,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    string? LatestError)
{
    public static FullSyncStatus Idle { get; } = new(
        "Idle",
        null,
        0,
        0,
        0,
        0,
        0,
        null,
        null,
        null);
}

public enum FullSyncStartOutcome
{
    Accepted,
    InvalidRequest,
    InvalidConfiguration,
    InvalidSource,
    AlreadyActive,
    WorkerUnavailable,
}

public sealed record FullSyncStartResult(
    FullSyncStartOutcome Outcome,
    string Message,
    FullSyncStatus Status);

public interface IFullSyncCoordinator
{
    FullSyncStatus Status { get; }

    FullSyncStartResult Start(Guid sourceUserId);
}
