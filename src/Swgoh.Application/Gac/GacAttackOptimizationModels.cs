using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public enum GacAttackOptimizationMode
{
    FillGaps = 0,
    RebuildPlanned = 1
}

public sealed record GacAttackOptimizationRecommendation(
    Guid DefenseId,
    string DefenseName,
    string Zone,
    Guid TeamPresetId,
    string TeamName,
    decimal Score,
    decimal StrategicCost,
    string Evidence,
    string Confidence,
    string Rationale,
    decimal? WinRate,
    decimal? OneShotRate,
    decimal? AverageBanners,
    int? Uses,
    decimal TacticalAdjustment = 0m,
    decimal? TeamAverageSpeed = null,
    decimal? DefenseAverageSpeed = null,
    decimal? TeamModSpeedBonus = null,
    decimal? DefenseModSpeedBonus = null,
    string DatacronStatus = "NotRequired");

public sealed record GacAttackOptimizationResult(
    GacAttackOptimizationMode Mode,
    bool Applied,
    int TargetDefenses,
    int RecommendedAttacks,
    int HistoricalMatches,
    decimal AverageScore,
    decimal? KnownAverageBanners,
    IReadOnlyCollection<Guid> UncoveredDefenseIds,
    IReadOnlyCollection<GacAttackOptimizationRecommendation> Recommendations,
    bool SearchLimitReached);

public sealed record GacAttackOptimizationLookup(
    CurrentGacOpponentStatus Status,
    string? Message,
    GacPlannerState? State,
    GacAttackOptimizationResult? Optimization)
{
    public bool IsAvailable =>
        Status == CurrentGacOpponentStatus.Found &&
        State is not null &&
        Optimization is not null;
}
