namespace Swgoh.Application.Investments;

public enum InvestmentModule
{
    Gac = 1,
    RiseOfEmpire = 2,
    Conquest = 3,
    Era = 4,
    Coliseum = 5
}

public sealed record InvestmentModuleImpact(
    InvestmentModule Module,
    decimal Score,
    string Reason,
    int? TargetRelicTier = null,
    int? TargetStars = null,
    bool ConcreteTarget = false);

public sealed record InvestmentCostEstimate(
    decimal CostIndex,
    string CostBand,
    int RelicSteps,
    int StarSteps,
    bool IsEstimate,
    string Summary);

public sealed record InvestmentResourceNeed(
    string ResourceId,
    string ResourceName,
    long Required,
    long Available,
    long Missing,
    bool Sufficient);

public sealed record InvestmentInventoryFit(
    DateTimeOffset CapturedAtUtc,
    string Source,
    bool MaterialsReady,
    decimal Coverage,
    int MissingResourceTypes,
    string Summary,
    IReadOnlyCollection<InvestmentResourceNeed> Resources);

public sealed record InvestmentRecommendation(
    int Rank,
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    int CurrentRelicTier,
    int CurrentStars,
    int? TargetRelicTier,
    int? TargetStars,
    decimal Score,
    decimal ValueScore,
    decimal InventoryAdjustedValueScore,
    decimal? ImpactPerCost,
    string Priority,
    string ValueRating,
    string SuggestedAction,
    string BenefitSummary,
    InvestmentCostEstimate? EstimatedCost,
    InvestmentInventoryFit? Inventory,
    IReadOnlyCollection<InvestmentModuleImpact> Impacts)
{
    public int ModuleCount => Impacts.Select(impact => impact.Module).Distinct().Count();
    public bool HasConcreteTarget => TargetRelicTier is not null || TargetStars is not null;
    public bool HasEstimatedCost => EstimatedCost is not null;
    public bool UsesRealInventory => Inventory is not null;
    public bool MaterialsReady => Inventory?.MaterialsReady is true;
}

public sealed record InvestmentModuleStatus(
    InvestmentModule Module,
    bool Available,
    int SignalCount,
    string Message);

public sealed record InvestmentOptimizationResult(
    long AllyCode,
    string PlayerName,
    DateTimeOffset RosterUpdatedAtUtc,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyCollection<InvestmentRecommendation> Recommendations,
    IReadOnlyCollection<InvestmentModuleStatus> Modules,
    DateTimeOffset? InventoryCapturedAtUtc,
    string? InventorySource)
{
    public int CrossModuleRecommendations => Recommendations.Count(item => item.ModuleCount >= 2);
    public int ConcreteTargets => Recommendations.Count(item => item.HasConcreteTarget);
    public int CostedRecommendations => Recommendations.Count(item => item.HasEstimatedCost);
    public int HighValueRecommendations => Recommendations.Count(item => item.InventoryAdjustedValueScore >= 60m);
    public int MaterialsReadyRecommendations => Recommendations.Count(item => item.MaterialsReady);
    public int InventoryAdjustedRecommendations => Recommendations.Count(item => item.UsesRealInventory);
    public bool HasInventorySnapshot => InventoryCapturedAtUtc is not null;
}

internal sealed record InvestmentSignal(
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    int CurrentRelicTier,
    int CurrentStars,
    InvestmentModule Module,
    decimal Score,
    string Reason,
    int? TargetRelicTier = null,
    int? TargetStars = null,
    bool ConcreteTarget = false);
