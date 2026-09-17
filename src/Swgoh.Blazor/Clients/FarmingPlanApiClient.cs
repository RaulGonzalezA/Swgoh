using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class FarmingPlanApiClient(HttpClient httpClient)
{
    public async Task<FarmingPlanViewModel> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/investments/players/{allyCode}/farming-plan",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FarmingPlanViewModel>(cancellationToken)
            ?? throw new InvalidOperationException("Farming plan API returned an empty response.");
    }

    public enum FarmingLaneViewModel
    {
        Credits = 1,
        Scavenger = 2,
        SignalData = 3,
        AdvancedRelic = 4
    }

    public enum InventoryResourceKindViewModel
    {
        Currency = 1,
        RelicMaterial = 2,
        SignalData = 3
    }

    public sealed record TargetDependencyViewModel(
        string DefinitionId,
        string Name,
        long Required);

    public sealed record ResourcePriorityViewModel(
        int Rank,
        string ResourceId,
        string ResourceName,
        InventoryResourceKindViewModel Kind,
        FarmingLaneViewModel Lane,
        string LaneLabel,
        string Action,
        long Required,
        long? Available,
        long? Missing,
        decimal? Coverage,
        int AffectedTargetCount,
        bool SharedBottleneck,
        string Priority,
        IReadOnlyCollection<TargetDependencyViewModel> Targets);

    public sealed record TargetPlanViewModel(
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

    public sealed record FarmingPlanViewModel(
        long AllyCode,
        DateTimeOffset GeneratedAtUtc,
        DateTimeOffset? InventoryCapturedAtUtc,
        string? InventorySource,
        int ActiveTargetCount,
        int RelicTargetCount,
        int StarOnlyTargetCount,
        int MissingResourceTypes,
        int SharedBottleneckCount,
        IReadOnlyCollection<ResourcePriorityViewModel> Resources,
        IReadOnlyCollection<TargetPlanViewModel> Targets,
        bool HasInventorySnapshot,
        bool HasActionableRelicPlan);
}
