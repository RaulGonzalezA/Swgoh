using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public sealed record GacLiveAttackState(
    string Id,
    string PlanId,
    long PlayerAllyCode,
    Guid AttackId,
    Guid DefenseId,
    int Attempt,
    GacAttackPlanStatus Status,
    int? Banners,
    string? Notes,
    IReadOnlyCollection<string> RemainingEnemyUnitDefinitionIds,
    bool PreloadedTurnMeter,
    DateTimeOffset RecordedAtUtc)
{
    public bool IsCleanup => Attempt > 1;

    public static GacLiveAttackState Create(
        string planId,
        long playerAllyCode,
        Guid attackId,
        Guid defenseId,
        int attempt,
        GacAttackPlanStatus status,
        int? banners,
        string? notes,
        IEnumerable<string> remainingEnemyUnitDefinitionIds,
        bool preloadedTurnMeter,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentOutOfRangeException.ThrowIfLessThan(playerAllyCode, 100_000_000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(playerAllyCode, 999_999_999);
        if (attackId == Guid.Empty)
        {
            throw new ArgumentException("Attack ID cannot be empty.", nameof(attackId));
        }

        if (defenseId == Guid.Empty)
        {
            throw new ArgumentException("Defense ID cannot be empty.", nameof(defenseId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        if (status is not (GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed))
        {
            throw new ArgumentException("Live attack status must be Won or Failed.", nameof(status));
        }

        if (banners is int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(banners));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100, nameof(banners));
        }

        string? normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (normalizedNotes?.Length > 500)
        {
            throw new ArgumentException("Attack notes cannot exceed 500 characters.", nameof(notes));
        }

        ArgumentNullException.ThrowIfNull(remainingEnemyUnitDefinitionIds);
        string[] survivors = status == GacAttackPlanStatus.Won
            ? []
            : [.. remainingEnemyUnitDefinitionIds
                .Select(id =>
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(remainingEnemyUnitDefinitionIds));
                    return id.Trim();
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (status == GacAttackPlanStatus.Failed && survivors.Length == 0)
        {
            throw new ArgumentException(
                "A failed attack must leave at least one enemy unit alive.",
                nameof(remainingEnemyUnitDefinitionIds));
        }

        return new GacLiveAttackState(
            BuildId(planId, attackId),
            planId.Trim(),
            playerAllyCode,
            attackId,
            defenseId,
            attempt,
            status,
            banners,
            normalizedNotes,
            survivors,
            status == GacAttackPlanStatus.Failed && preloadedTurnMeter,
            recordedAtUtc);
    }

    public static string BuildId(string planId, Guid attackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        if (attackId == Guid.Empty)
        {
            throw new ArgumentException("Attack ID cannot be empty.", nameof(attackId));
        }

        return $"{planId.Trim()}:{attackId:D}";
    }
}

public interface IGacLiveAttackStateRepository
{
    Task<IReadOnlyCollection<GacLiveAttackState>> GetByPlanAsync(
        string planId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        GacLiveAttackState state,
        CancellationToken cancellationToken = default);
}
