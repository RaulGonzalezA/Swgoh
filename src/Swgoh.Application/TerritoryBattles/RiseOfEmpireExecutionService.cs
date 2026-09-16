using Swgoh.Application.Abstractions;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

public enum RiseOfEmpireExecutionStatus
{
    Active = 0,
    Closed = 1
}

public enum RiseOfEmpireMissionExecutionState
{
    NotAttempted = 0,
    InProgress = 1,
    Finished = 2
}

public sealed record RiseOfEmpireMissionExecutionResult(
    long PlayerAllyCode,
    string PlayerName,
    int Phase,
    string PlanetId,
    string PlanetName,
    string MissionId,
    string MissionName,
    string? TeamName,
    RiseOfEmpireMissionExecutionState State,
    int CompletedWaves,
    int TotalWaves,
    long? TerritoryPoints,
    string? Notes,
    DateTimeOffset UpdatedAtUtc)
{
    public string Key => KeyFor(PlayerAllyCode, Phase, PlanetId, MissionId);

    public static string KeyFor(long allyCode, int phase, string planetId, string missionId) =>
        $"{allyCode}:{phase}:{planetId}:{missionId}";
}

public sealed record RiseOfEmpireExecutionSession(
    string Id,
    string GuildId,
    string GuildName,
    string Label,
    RiseOfEmpireExecutionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    IReadOnlyCollection<RiseOfEmpireMissionExecutionResult> Results)
{
    public int FinishedAttempts => Results.Count(result => result.State == RiseOfEmpireMissionExecutionState.Finished);
    public int InProgressAttempts => Results.Count(result => result.State == RiseOfEmpireMissionExecutionState.InProgress);
    public long RecordedTerritoryPoints => Results.Sum(result => result.TerritoryPoints ?? 0);
}

public sealed record RiseOfEmpireMissionResultCommand(
    long PlayerAllyCode,
    string PlayerName,
    int Phase,
    string PlanetId,
    string PlanetName,
    string MissionId,
    string MissionName,
    string? TeamName,
    RiseOfEmpireMissionExecutionState State,
    int CompletedWaves,
    int TotalWaves,
    long? TerritoryPoints,
    string? Notes);

public interface IRiseOfEmpireExecutionRepository
{
    Task<RiseOfEmpireExecutionSession?> GetActiveAsync(string guildId, CancellationToken cancellationToken = default);
    Task<RiseOfEmpireExecutionSession?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RiseOfEmpireExecutionSession>> GetHistoryAsync(
        string guildId,
        int limit,
        CancellationToken cancellationToken = default);
    Task UpsertAsync(RiseOfEmpireExecutionSession session, CancellationToken cancellationToken = default);
}

public interface IRiseOfEmpireExecutionService
{
    Task<RiseOfEmpireExecutionSession?> GetActiveAsync(long allyCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RiseOfEmpireExecutionSession>> GetHistoryAsync(long allyCode, CancellationToken cancellationToken = default);
    Task<RiseOfEmpireExecutionSession> StartAsync(long allyCode, string? label, CancellationToken cancellationToken = default);
    Task<RiseOfEmpireExecutionSession> UpdateMissionAsync(
        long allyCode,
        string sessionId,
        RiseOfEmpireMissionResultCommand command,
        CancellationToken cancellationToken = default);
    Task<RiseOfEmpireExecutionSession> CloseAsync(long allyCode, string sessionId, CancellationToken cancellationToken = default);
}

internal sealed class RiseOfEmpireExecutionService(
    IPlayerProfileService playerProfileService,
    IRiseOfEmpireExecutionRepository repository,
    IClock clock) : IRiseOfEmpireExecutionService
{
    private const int HistoryLimit = 12;

    public async Task<RiseOfEmpireExecutionSession?> GetActiveAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        PlayerProfile player = await RequireGuildPlayerAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return await repository.GetActiveAsync(player.GuildId!, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<RiseOfEmpireExecutionSession>> GetHistoryAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        PlayerProfile player = await RequireGuildPlayerAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return await repository.GetHistoryAsync(player.GuildId!, HistoryLimit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RiseOfEmpireExecutionSession> StartAsync(
        long allyCode,
        string? label,
        CancellationToken cancellationToken = default)
    {
        PlayerProfile player = await RequireGuildPlayerAsync(allyCode, cancellationToken).ConfigureAwait(false);
        RiseOfEmpireExecutionSession? current = await repository
            .GetActiveAsync(player.GuildId!, cancellationToken)
            .ConfigureAwait(false);
        if (current is not null)
        {
            return current;
        }

        DateTimeOffset now = clock.UtcNow;
        string sessionLabel = string.IsNullOrWhiteSpace(label)
            ? $"RotE {now:yyyy-MM-dd}"
            : label.Trim();
        if (sessionLabel.Length > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(label), "La etiqueta no puede superar 80 caracteres.");
        }

        var session = new RiseOfEmpireExecutionSession(
            Guid.NewGuid().ToString("N"),
            player.GuildId!,
            player.GuildName ?? "Gremio",
            sessionLabel,
            RiseOfEmpireExecutionStatus.Active,
            now,
            now,
            null,
            []);
        await repository.UpsertAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task<RiseOfEmpireExecutionSession> UpdateMissionAsync(
        long allyCode,
        string sessionId,
        RiseOfEmpireMissionResultCommand command,
        CancellationToken cancellationToken = default)
    {
        PlayerProfile player = await RequireGuildPlayerAsync(allyCode, cancellationToken).ConfigureAwait(false);
        RiseOfEmpireExecutionSession session = await RequireSessionAsync(player, sessionId, cancellationToken).ConfigureAwait(false);
        if (session.Status != RiseOfEmpireExecutionStatus.Active)
        {
            throw new InvalidOperationException("La ejecución RotE ya está cerrada.");
        }

        Validate(command);
        DateTimeOffset now = clock.UtcNow;
        string key = RiseOfEmpireMissionExecutionResult.KeyFor(
            command.PlayerAllyCode,
            command.Phase,
            command.PlanetId,
            command.MissionId);
        var result = new RiseOfEmpireMissionExecutionResult(
            command.PlayerAllyCode,
            command.PlayerName.Trim(),
            command.Phase,
            command.PlanetId.Trim(),
            command.PlanetName.Trim(),
            command.MissionId.Trim(),
            command.MissionName.Trim(),
            string.IsNullOrWhiteSpace(command.TeamName) ? null : command.TeamName.Trim(),
            command.State,
            command.CompletedWaves,
            command.TotalWaves,
            command.TerritoryPoints,
            NormalizeNotes(command.Notes),
            now);
        RiseOfEmpireMissionExecutionResult[] results =
        [
            .. session.Results.Where(item => !string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)),
            result
        ];
        RiseOfEmpireExecutionSession updated = session with { Results = results, UpdatedAtUtc = now };
        await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<RiseOfEmpireExecutionSession> CloseAsync(
        long allyCode,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        PlayerProfile player = await RequireGuildPlayerAsync(allyCode, cancellationToken).ConfigureAwait(false);
        RiseOfEmpireExecutionSession session = await RequireSessionAsync(player, sessionId, cancellationToken).ConfigureAwait(false);
        if (session.Status == RiseOfEmpireExecutionStatus.Closed)
        {
            return session;
        }

        DateTimeOffset now = clock.UtcNow;
        RiseOfEmpireExecutionSession closed = session with
        {
            Status = RiseOfEmpireExecutionStatus.Closed,
            UpdatedAtUtc = now,
            ClosedAtUtc = now
        };
        await repository.UpsertAsync(closed, cancellationToken).ConfigureAwait(false);
        return closed;
    }

    private async Task<PlayerProfile> RequireGuildPlayerAsync(long allyCode, CancellationToken cancellationToken)
    {
        if (allyCode <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode));
        }

        PlayerProfile? player = await playerProfileService.GetAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            throw new KeyNotFoundException("No existe el jugador importado.");
        }

        if (string.IsNullOrWhiteSpace(player.GuildId))
        {
            throw new InvalidOperationException("El jugador no tiene un gremio disponible.");
        }

        return player;
    }

    private async Task<RiseOfEmpireExecutionSession> RequireSessionAsync(
        PlayerProfile player,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        RiseOfEmpireExecutionSession? session = await repository
            .GetByIdAsync(sessionId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (session is null || !string.Equals(session.GuildId, player.GuildId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("No existe la ejecución RotE indicada para este gremio.");
        }

        return session;
    }

    private static void Validate(RiseOfEmpireMissionResultCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PlayerAllyCode <= 0 || command.Phase is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Jugador o fase no válidos.");
        }

        if (string.IsNullOrWhiteSpace(command.PlayerName)
            || string.IsNullOrWhiteSpace(command.PlanetId)
            || string.IsNullOrWhiteSpace(command.PlanetName)
            || string.IsNullOrWhiteSpace(command.MissionId)
            || string.IsNullOrWhiteSpace(command.MissionName))
        {
            throw new ArgumentException("El resultado debe identificar jugador, planeta y misión.", nameof(command));
        }

        if (command.TotalWaves is < 1 or > 10 || command.CompletedWaves < 0 || command.CompletedWaves > command.TotalWaves)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "El número de oleadas no es válido.");
        }

        if (command.TerritoryPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Los Territory Points no pueden ser negativos.");
        }
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        string normalized = notes.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}
