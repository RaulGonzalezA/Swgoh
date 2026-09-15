using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public enum GacAttackOptimizationMode
{
    FillGaps = 0,
    RebuildPlanned = 1
}

public sealed record GacCounterCandidateAnalysis(
    int Rank,
    Guid TeamPresetId,
    string TeamName,
    decimal Score,
    decimal EstimatedWinProbability,
    decimal? ExpectedBanners,
    string Risk,
    string TimeoutRisk,
    decimal StrategicCost,
    decimal CriticalPieceCost,
    string Evidence,
    string Confidence,
    string Rationale,
    string DatacronStatus,
    decimal TacticalAdjustment,
    decimal PersonalAdjustment,
    int FutureDefensesAtRisk);

public sealed record GacCounterDefenseAnalysis(
    Guid DefenseId,
    string DefenseName,
    string Zone,
    IReadOnlyCollection<GacCounterCandidateAnalysis> Candidates);

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
    string DatacronStatus = "NotRequired",
    decimal BaseStrategicCost = 0m,
    decimal OpportunityCost = 0m,
    int StrategicAlternatives = 0,
    int FutureDefensesAtRisk = 0,
    string StrategicRationale = "",
    decimal PersonalAdjustment = 0m,
    int PersonalSamples = 0,
    int PersonalWins = 0,
    decimal? PersonalWinRate = null,
    decimal? PersonalOneShotRate = null,
    decimal? PersonalAverageBanners = null,
    string PersonalScope = "None",
    string PersonalRationale = "",
    decimal EstimatedWinProbability = 0m,
    string Risk = "High",
    string TimeoutRisk = "Unknown",
    decimal CriticalPieceCost = 0m);

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
    bool SearchLimitReached,
    IReadOnlyCollection<GacCounterDefenseAnalysis>? CounterAnalyses = null)
{
    public IReadOnlyCollection<GacCounterDefenseAnalysis> CounterEngine => CounterAnalyses ?? [];
}

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
