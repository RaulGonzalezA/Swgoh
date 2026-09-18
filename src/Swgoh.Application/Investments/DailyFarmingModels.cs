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

public sealed record InvestmentDailyFarmingPlan(
    long AllyCode,
    DateTimeOffset GeneratedAtUtc,
    bool HasInventorySnapshot,
    DateTimeOffset? InventoryCapturedAtUtc,
    int ActiveTargetCount,
    int MissingResourceTypes,
    IReadOnlyCollection<DailyEnergyBaseline> EnergyBaselines,
    IReadOnlyCollection<DailyFarmingAction> Actions,
    string Summary,
    string Limitation);
