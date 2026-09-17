using System.Net;
using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class InvestmentApiClient(HttpClient httpClient)
{
    public async Task<OptimizationViewModel?> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/investments/players/{allyCode}/current",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OptimizationViewModel>(cancellationToken);
    }

    public enum InvestmentModuleViewModel
    {
        Gac = 1,
        RiseOfEmpire = 2,
        Conquest = 3,
        Era = 4,
        Coliseum = 5
    }

    public sealed record ModuleImpactViewModel(
        InvestmentModuleViewModel Module,
        decimal Score,
        string Reason,
        int? TargetRelicTier,
        int? TargetStars,
        bool ConcreteTarget);

    public sealed record CostEstimateViewModel(
        decimal CostIndex,
        string CostBand,
        int RelicSteps,
        int StarSteps,
        bool IsEstimate,
        string Summary);

    public sealed record RecommendationViewModel(
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
        decimal? ImpactPerCost,
        string Priority,
        string ValueRating,
        string SuggestedAction,
        string BenefitSummary,
        CostEstimateViewModel? EstimatedCost,
        IReadOnlyCollection<ModuleImpactViewModel> Impacts,
        int ModuleCount,
        bool HasConcreteTarget,
        bool HasEstimatedCost);

    public sealed record ModuleStatusViewModel(
        InvestmentModuleViewModel Module,
        bool Available,
        int SignalCount,
        string Message);

    public sealed record OptimizationViewModel(
        long AllyCode,
        string PlayerName,
        DateTimeOffset RosterUpdatedAtUtc,
        DateTimeOffset GeneratedAtUtc,
        IReadOnlyCollection<RecommendationViewModel> Recommendations,
        IReadOnlyCollection<ModuleStatusViewModel> Modules,
        int CrossModuleRecommendations,
        int ConcreteTargets,
        int CostedRecommendations,
        int HighValueRecommendations);
}
