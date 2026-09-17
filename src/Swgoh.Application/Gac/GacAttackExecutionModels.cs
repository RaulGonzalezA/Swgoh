using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public sealed record ExecuteGacAttackResult(
    GacAttackPlanStatus Status,
    int? Banners,
    string? Notes,
    IReadOnlyCollection<string>? RemainingEnemyUnitDefinitionIds = null,
    bool PreloadedTurnMeter = false);

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
