using Swgoh.Domain.Conquest;

namespace Swgoh.Application.Conquest;

public sealed record SaveConquestFeat(
    Guid? Id,
    string Name,
    ConquestFeatScope Scope,
    int? Sector,
    int Points,
    int Target,
    int Progress,
    int ExpectedProgressPerBattle,
    ConquestFeatRuleType RuleType,
    string? Faction,
    IReadOnlyCollection<string> UnitDefinitionIds,
    int MinimumMatchingUnits);

public sealed record SaveConquestPlan(
    string EventId,
    string Name,
    ConquestDifficulty Difficulty,
    IReadOnlyCollection<SaveConquestFeat> Feats);

public sealed record ConquestFeatDetails(
    Guid Id,
    string Name,
    ConquestFeatScope Scope,
    int? Sector,
    int Points,
    int Target,
    int Progress,
    int Remaining,
    int ExpectedProgressPerBattle,
    ConquestFeatRule Rule,
    bool IsComplete);

public sealed record ConquestPlanDetails(
    string Id,
    long AllyCode,
    string EventId,
    string Name,
    ConquestDifficulty Difficulty,
    IReadOnlyCollection<ConquestFeatDetails> Feats,
    int CompletedFeats,
    int TotalFeats,
    int EarnedFeatPoints,
    int AvailableFeatPoints,
    DateTimeOffset UpdatedAtUtc);

public sealed record ConquestOptimizationUnit(
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    int RelicTier,
    long GalacticPower,
    decimal? Speed,
    IReadOnlyCollection<string> Factions);

public sealed record ConquestFeatContribution(
    Guid FeatId,
    string FeatName,
    int Points,
    int Remaining,
    int ExpectedProgress,
    decimal PointValueThisBattle);

public sealed record ConquestTeamRecommendation(
    int Rank,
    decimal Score,
    decimal FeatEfficiency,
    long TeamGalacticPower,
    decimal? AverageSpeed,
    IReadOnlyCollection<ConquestOptimizationUnit> Team,
    IReadOnlyCollection<ConquestFeatContribution> AdvancesFeats,
    string Rationale);

public sealed record ConquestOptimizationResult(
    long AllyCode,
    string EventId,
    int PendingFeats,
    int CandidateCharacters,
    IReadOnlyCollection<ConquestTeamRecommendation> Recommendations,
    IReadOnlyCollection<Guid> UncoveredFeatIds);
