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

    public async Task<InventoryViewModel> GetInventoryAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/investments/players/{allyCode}/inventory",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InventoryViewModel>(cancellationToken)
            ?? throw new InvalidOperationException("Inventory API returned an empty response.");
    }

    public async Task<InventoryViewModel> SaveInventoryAsync(
        long allyCode,
        InventoryViewModel inventory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        InventoryUpdateViewModel request = new(
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(inventory.Source) ? "manual" : inventory.Source.Trim(),
            [
                .. inventory.Resources.Select(resource => new InventoryResourceUpdateViewModel(
                    resource.Id,
                    resource.Name,
                    resource.Quantity))
            ]);
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/v1/investments/players/{allyCode}/inventory",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InventoryViewModel>(cancellationToken)
            ?? throw new InvalidOperationException("Inventory API returned an empty response after saving.");
    }

    public enum InvestmentModuleViewModel
    {
        Gac = 1,
        RiseOfEmpire = 2,
        Conquest = 3,
        Era = 4,
        Coliseum = 5
    }

    public enum InventoryResourceKindViewModel
    {
        Currency = 1,
        RelicMaterial = 2,
        SignalData = 3
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

    public sealed record ResourceNeedViewModel(
        string ResourceId,
        string ResourceName,
        long Required,
        long Available,
        long Missing,
        bool Sufficient);

    public sealed record InventoryFitViewModel(
        DateTimeOffset CapturedAtUtc,
        string Source,
        bool CanCompleteNow,
        decimal Coverage,
        int MissingResourceTypes,
        string Summary,
        IReadOnlyCollection<ResourceNeedViewModel> Resources);

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
        decimal InventoryAdjustedValueScore,
        decimal? ImpactPerCost,
        string Priority,
        string ValueRating,
        string SuggestedAction,
        string BenefitSummary,
        CostEstimateViewModel? EstimatedCost,
        InventoryFitViewModel? Inventory,
        IReadOnlyCollection<ModuleImpactViewModel> Impacts,
        int ModuleCount,
        bool HasConcreteTarget,
        bool HasEstimatedCost,
        bool UsesRealInventory,
        bool CanCompleteNow);

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
        DateTimeOffset? InventoryCapturedAtUtc,
        string? InventorySource,
        int CrossModuleRecommendations,
        int ConcreteTargets,
        int CostedRecommendations,
        int HighValueRecommendations,
        int ReadyNowRecommendations,
        int InventoryAdjustedRecommendations,
        bool HasInventorySnapshot);

    public sealed class InventoryViewModel
    {
        public bool HasSnapshot { get; set; }
        public DateTimeOffset? CapturedAtUtc { get; set; }
        public string? Source { get; set; }
        public List<InventoryResourceViewModel> Resources { get; set; } = [];
    }

    public sealed class InventoryResourceViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public InventoryResourceKindViewModel Kind { get; set; }
        public int SortOrder { get; set; }
        public long Quantity { get; set; }
    }

    private sealed record InventoryUpdateViewModel(
        DateTimeOffset? CapturedAtUtc,
        string? Source,
        IReadOnlyCollection<InventoryResourceUpdateViewModel> Resources);

    private sealed record InventoryResourceUpdateViewModel(
        string Id,
        string Name,
        long Quantity);
}
