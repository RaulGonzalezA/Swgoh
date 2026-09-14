namespace Swgoh.Application.Conquest;

public sealed record ConquestDailyPlanRequest(int MaxBattles = 6);

public sealed record ConquestDailyFeatProgress(
    Guid FeatId,
    string FeatName,
    int Points,
    int BeforeProgress,
    int AfterProgress,
    int Target,
    bool CompletedByBattle,
    int RewardPointsGranted);

public sealed record ConquestDailyPlanStep(
    int BattleNumber,
    decimal Score,
    decimal FeatEfficiency,
    int EnergyCost,
    int CumulativeEnergySpent,
    int RewardPointsEarned,
    int ProjectedRewardPointsAfterBattle,
    decimal RewardPointsPerEnergy,
    bool RewardTargetReached,
    decimal AverageStaminaBefore,
    decimal AverageStaminaAfter,
    int ReserveRiskUnits,
    bool ChangesTeamFromPrevious,
    bool ChangesLoadoutFromPrevious,
    ConquestDiskRecommendation? DiskLoadout,
    IReadOnlyCollection<ConquestOptimizationUnit> Team,
    IReadOnlyCollection<ConquestDailyFeatProgress> FeatProgress,
    string Rationale);

public sealed record ConquestDailyRecoveryUnit(
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    int FinalStamina,
    int ReserveFloorPercent);

public sealed record ConquestDailyPlanResult(
    long AllyCode,
    string EventId,
    int RequestedBattles,
    int PlannedBattles,
    int StartingPendingFeats,
    int ProjectedCompletedFeats,
    int ProjectedRemainingFeats,
    int? AvailableEnergy,
    int EnergyCostPerBattle,
    int EnergySpent,
    int? EnergyRemaining,
    int StartingRewardPoints,
    int ProjectedRewardPoints,
    int ProjectedRewardPointsGained,
    int? TargetRewardPoints,
    string? RewardTargetName,
    bool RewardTargetReached,
    decimal RewardPointsPerEnergy,
    string StopReason,
    IReadOnlyCollection<ConquestDailyPlanStep> Steps,
    IReadOnlyCollection<ConquestDailyRecoveryUnit> RecoveryPriority,
    IReadOnlyCollection<Guid> RemainingFeatIds);
