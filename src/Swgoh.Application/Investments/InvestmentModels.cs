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
    string Priority,
    string SuggestedAction,
    IReadOnlyCollection<InvestmentModuleImpact> Impacts)
{
    public int ModuleCount => Impacts.Select(impact => impact.Module).Distinct().Count();
    public bool HasConcreteTarget => TargetRelicTier is not null || TargetStars is not null;
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
    IReadOnlyCollection<InvestmentModuleStatus> Modules)
{
    public int CrossModuleRecommendations => Recommendations.Count(item => item.ModuleCount >= 2);
    public int ConcreteTargets => Recommendations.Count(item => item.HasConcreteTarget);
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
