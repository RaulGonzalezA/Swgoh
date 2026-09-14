using System.Net;
using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class ConquestDailyPlanApiClient(HttpClient httpClient)
{
    public async Task<DailyPlanViewModel?> BuildAsync(
        long allyCode,
        int maxBattles,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/v1/conquest/players/{allyCode}/current/daily-plan",
            new DailyPlanRequest(maxBattles),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DailyPlanViewModel>(cancellationToken);
    }

    public sealed record DailyPlanRequest(int MaxBattles);

    public sealed record DailyPlanViewModel(
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
        IReadOnlyCollection<DailyPlanStepViewModel> Steps,
        IReadOnlyCollection<RecoveryUnitViewModel> RecoveryPriority,
        IReadOnlyCollection<Guid> RemainingFeatIds);

    public sealed record DailyPlanStepViewModel(
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
        DiskRecommendationViewModel? DiskLoadout,
        IReadOnlyCollection<UnitViewModel> Team,
        IReadOnlyCollection<FeatProgressViewModel> FeatProgress,
        string Rationale);

    public sealed record FeatProgressViewModel(
        Guid FeatId,
        string FeatName,
        int Points,
        int BeforeProgress,
        int AfterProgress,
        int Target,
        bool CompletedByBattle,
        int RewardPointsGranted);

    public sealed record RecoveryUnitViewModel(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        int FinalStamina,
        int ReserveFloorPercent);

    public sealed record DiskRecommendationViewModel(
        Guid LoadoutId,
        string LoadoutName,
        int CapacityUsed,
        int CapacityLimit,
        decimal PlannerBonus,
        IReadOnlyCollection<DataDiskViewModel> Disks,
        IReadOnlyCollection<Guid> MatchedFeatIds);

    public sealed record DataDiskViewModel(
        Guid Id,
        string Name,
        int CapacityCost,
        decimal PlannerBonus,
        string TargetType,
        string? Faction,
        IReadOnlyCollection<string> UnitDefinitionIds,
        int MinimumMatchingUnits,
        IReadOnlyCollection<Guid> SupportedFeatIds,
        string? Notes);

    public sealed record UnitViewModel(
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
}
