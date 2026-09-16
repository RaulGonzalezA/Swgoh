namespace Swgoh.Application.TerritoryBattles;

public enum RiseOfEmpireGuildSyncStatus
{
    Queued = 0,
    DiscoveringMembers = 1,
    RefreshingMembers = 2,
    BuildingPlan = 3,
    Completed = 4,
    Failed = 5
}

public sealed record RiseOfEmpireGuildSyncProgress(
    RiseOfEmpireGuildSyncStatus Status,
    int TotalMembers,
    int CompletedMembers,
    int FailedMembers,
    string? CurrentMember = null,
    string? GuildId = null,
    string? GuildName = null);

public sealed record RiseOfEmpireGuildSyncJob(
    string Id,
    long AllyCode,
    RiseOfEmpireGuildSyncStatus Status,
    int TotalMembers,
    int CompletedMembers,
    int FailedMembers,
    string? CurrentMember,
    string? GuildId,
    string? GuildName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? Error = null)
{
    public bool IsTerminal => Status is RiseOfEmpireGuildSyncStatus.Completed or RiseOfEmpireGuildSyncStatus.Failed;

    public int ProgressPercent => Status == RiseOfEmpireGuildSyncStatus.Completed
        ? 100
        : TotalMembers <= 0
            ? 0
            : Math.Clamp((int)Math.Round(CompletedMembers * 100m / TotalMembers), 0, 99);
}

public interface IRiseOfEmpireGuildSyncProgressSink
{
    ValueTask ReportAsync(
        RiseOfEmpireGuildSyncProgress progress,
        CancellationToken cancellationToken = default);
}

public interface IRiseOfEmpireGuildSyncRunner
{
    Task<RiseOfEmpireGuildAnalysis?> SyncAsync(
        long allyCode,
        IRiseOfEmpireGuildSyncProgressSink progress,
        CancellationToken cancellationToken = default);
}

public interface IRiseOfEmpireGuildSyncQueue
{
    Task<RiseOfEmpireGuildSyncJob> StartAsync(
        long allyCode,
        CancellationToken cancellationToken = default);

    Task<RiseOfEmpireGuildSyncJob?> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    Task<RiseOfEmpireGuildSyncJob?> GetLatestAsync(
        long allyCode,
        CancellationToken cancellationToken = default);
}

public interface IRiseOfEmpireGuildSyncJobRepository
{
    Task<RiseOfEmpireGuildSyncJob?> FindByIdAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    Task<RiseOfEmpireGuildSyncJob?> FindLatestByAllyCodeAsync(
        long allyCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<RiseOfEmpireGuildSyncJob>> FindRecoverableAsync(
        int maxJobs = 16,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        RiseOfEmpireGuildSyncJob job,
        CancellationToken cancellationToken = default);
}
