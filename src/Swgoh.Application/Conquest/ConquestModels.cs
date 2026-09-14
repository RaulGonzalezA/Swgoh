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

public sealed record SaveConquestUnitStamina(string DefinitionId, int CurrentPercent);

public sealed record SaveConquestDataDisk(
    Guid? Id,
    string Name,
    int CapacityCost,
    decimal PlannerBonus,
    ConquestDataDiskTargetType TargetType,
    string? Faction,
    IReadOnlyCollection<string> UnitDefinitionIds,
    int MinimumMatchingUnits,
    IReadOnlyCollection<Guid> SupportedFeatIds,
    string? Notes);

public sealed record SaveConquestDiskLoadout(
    Guid? Id,
    string Name,
    IReadOnlyCollection<Guid> DiskIds);

public sealed record SaveConquestPlan(
    string EventId,
    string Name,
    ConquestDifficulty Difficulty,
    IReadOnlyCollection<SaveConquestFeat> Feats,
    int StaminaCostPerBattle = ConquestPlan.DefaultStaminaCostPerBattle,
    int ReserveFloorPercent = ConquestPlan.DefaultReserveFloorPercent,
    IReadOnlyCollection<SaveConquestUnitStamina>? Stamina = null,
    int DiskCapacityLimit = ConquestPlan.DefaultDiskCapacityLimit,
    IReadOnlyCollection<SaveConquestDataDisk>? DataDisks = null,
    IReadOnlyCollection<SaveConquestDiskLoadout>? DiskLoadouts = null,
    int? AvailableEnergy = null,
    int EnergyCostPerBattle = ConquestPlan.DefaultEnergyCostPerBattle,
    int CurrentRewardPoints = 0,
    int? TargetRewardPoints = null,
    string? RewardTargetName = null);

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
    int StaminaCostPerBattle,
    int ReserveFloorPercent,
    IReadOnlyCollection<ConquestUnitStamina> Stamina,
    int DiskCapacityLimit,
    IReadOnlyCollection<ConquestDataDisk> DataDisks,
    IReadOnlyCollection<ConquestDiskLoadout> DiskLoadouts,
    int? AvailableEnergy,
    int EnergyCostPerBattle,
    int CurrentRewardPoints,
    int? TargetRewardPoints,
    string? RewardTargetName,
    DateTimeOffset UpdatedAtUtc);

public sealed record ConquestOptimizationUnit(
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    int RelicTier,
    long GalacticPower,
    decimal? Speed,
    IReadOnlyCollection<string> Factions,
    int CurrentStamina,
    int ExpectedPostBattleStamina,
    bool BelowReserveAfterBattle);

public sealed record ConquestFeatContribution(
    Guid FeatId,
    string FeatName,
    int Points,
    int Remaining,
    int ExpectedProgress,
    decimal PointValueThisBattle);

public sealed record ConquestDiskRecommendation(
    Guid LoadoutId,
    string LoadoutName,
    int CapacityUsed,
    int CapacityLimit,
    decimal PlannerBonus,
    IReadOnlyCollection<ConquestDataDisk> Disks,
    IReadOnlyCollection<Guid> MatchedFeatIds);

public sealed record ConquestTeamRecommendation(
    int Rank,
    decimal Score,
    decimal FeatEfficiency,
    long TeamGalacticPower,
    decimal? AverageSpeed,
    decimal AverageStamina,
    decimal ExpectedPostBattleAverageStamina,
    decimal StaminaOpportunityCost,
    int ReserveRiskUnits,
    ConquestDiskRecommendation? DiskLoadout,
    IReadOnlyCollection<ConquestOptimizationUnit> Team,
    IReadOnlyCollection<ConquestFeatContribution> AdvancesFeats,
    string Rationale);

public sealed record ConquestOptimizationResult(
    long AllyCode,
    string EventId,
    int PendingFeats,
    int CandidateCharacters,
    int StaminaCostPerBattle,
    int ReserveFloorPercent,
    int DiskCapacityLimit,
    IReadOnlyCollection<ConquestTeamRecommendation> Recommendations,
    IReadOnlyCollection<Guid> UncoveredFeatIds);
