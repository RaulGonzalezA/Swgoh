namespace Swgoh.Application.Investments;

public enum DailyFarmingChannel
{
    CantinaEnergy = 1,
    NormalEnergy = 2,
    FleetEnergy = 3,
    Scavenger = 4,
    Stores = 5,
    Credits = 6
}

public enum DailyFarmingPrecision
{
    Exact = 1,
    Guided = 2,
    Check = 3
}

public sealed record DailyEnergyBaseline(
    DailyFarmingChannel Channel,
    string Label,
    int RegenerationPerDay,
    int BonusEnergyPerDay,
    int BaselineFreeEnergy,
    string Note);

public sealed record DailyFarmingAction(
    int Rank,
    DailyFarmingChannel Channel,
    string ChannelLabel,
    DailyFarmingPrecision Precision,
    string PrecisionLabel,
    string Title,
    string Action,
    string? ResourceId,
    string? ResourceName,
    string Source,
    int? EnergyCostPerAttempt,
    int? BaselineFreeEnergy,
    long? Missing,
    int AffectedTargetCount,
    bool SharedBottleneck,
    string Priority,
    string StopCondition);

public sealed record DailyRefreshRecommendation(
    DailyFarmingChannel Channel,
    string ChannelLabel,
    int RefreshCount,
    int CrystalCost,
    int EnergyGained,
    int BaselineFreeEnergy,
    int PlannedDailyEnergy,
    int? NextRefreshCost,
    string Reason);

public sealed record DailyCrystalBudgetPlan(
    int DailyCrystalBudget,
    int CrystalsSpent,
    int CrystalsUnspent,
    string ProfileLabel,
    IReadOnlyCollection<DailyRefreshRecommendation> Refreshes)
{
    public int RefreshCount => Refreshes.Sum(refresh => refresh.RefreshCount);
    public int EnergyGained => Refreshes.Sum(refresh => refresh.EnergyGained);
}

public sealed record InvestmentDailyFarmingPlan(
    long AllyCode,
    DateTimeOffset GeneratedAtUtc,
    bool HasInventorySnapshot,
    DateTimeOffset? InventoryCapturedAtUtc,
    int ActiveTargetCount,
    int MissingResourceTypes,
    IReadOnlyCollection<DailyEnergyBaseline> EnergyBaselines,
    IReadOnlyCollection<DailyFarmingAction> Actions,
    DailyCrystalBudgetPlan CrystalBudget,
    string Summary,
    string Limitation);
