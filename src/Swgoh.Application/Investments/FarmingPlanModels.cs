namespace Swgoh.Application.Investments;

public enum FarmingLane
{
    Credits = 1,
    Scavenger = 2,
    SignalData = 3,
    AdvancedRelic = 4
}

public sealed record FarmingTargetDependency(
    string DefinitionId,
    string Name,
    long Required);

public sealed record FarmingResourcePriority(
    int Rank,
    string ResourceId,
    string ResourceName,
    InventoryResourceKind Kind,
    FarmingLane Lane,
    string LaneLabel,
    string Action,
    long Required,
    long? Available,
    long? Missing,
    decimal? Coverage,
    int AffectedTargetCount,
    bool SharedBottleneck,
    string Priority,
    IReadOnlyCollection<FarmingTargetDependency> Targets);

public sealed record FarmingTargetPlan(
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    int CurrentRelicTier,
    int? TargetRelicTier,
    int CurrentStars,
    int? TargetStars,
    int RelicStepsRemaining,
    int StarStepsRemaining,
    decimal? MaterialCoverage,
    int BlockingResourceTypes,
    int SharedBottlenecks,
    bool RelicMaterialsTracked,
    string Summary);

public sealed record InvestmentFarmingPlan(
    long AllyCode,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? InventoryCapturedAtUtc,
    string? InventorySource,
    int ActiveTargetCount,
    int RelicTargetCount,
    int StarOnlyTargetCount,
    int MissingResourceTypes,
    int SharedBottleneckCount,
    IReadOnlyCollection<FarmingResourcePriority> Resources,
    IReadOnlyCollection<FarmingTargetPlan> Targets)
{
    public bool HasInventorySnapshot => InventoryCapturedAtUtc is not null;
    public bool HasActionableRelicPlan => RelicTargetCount > 0 && Resources.Count > 0;
}
