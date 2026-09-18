using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public sealed record ExecuteGacAttackResult(
    GacAttackPlanStatus Status,
    int? Banners,
    string? Notes,
    IReadOnlyCollection<string>? RemainingEnemyUnitDefinitionIds = null,
    bool PreloadedTurnMeter = false);

public sealed record GacWarRoomReplanSummary(
    bool Applied,
    int PreviousPendingAttacks,
    int CurrentPendingAttacks,
    int ReplacedPendingAttacks,
    int CoveredDefenses,
    int UncoveredDefenses,
    IReadOnlyCollection<Guid> ReplannedDefenseIds);

public sealed record GacAttackExecutionResult(
    Guid AttackId,
    GacAttackPlanStatus Status,
    int? Banners,
    string? Notes,
    IReadOnlyCollection<string> RemainingEnemyUnitDefinitionIds,
    bool PreloadedTurnMeter,
    bool IsCleanup,
    GacPlannerState State,
    GacAttackOptimizationResult? Optimization,
    GacAttackOptimizationRecommendation? NextRecommendation,
    GacWarRoomReplanSummary? Replan,
    IReadOnlyCollection<string>? Warnings = null)
{
    public IReadOnlyCollection<string> PostCommitWarnings => Warnings ?? [];
}

public sealed record GacAttackExecutionLookup(
    CurrentGacOpponentStatus Status,
    string? Message,
    GacAttackExecutionResult? Execution)
{
    public bool IsAvailable => Status == CurrentGacOpponentStatus.Found && Execution is not null;
}
