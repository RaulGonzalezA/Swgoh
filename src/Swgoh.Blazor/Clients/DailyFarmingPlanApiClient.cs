using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class DailyFarmingPlanApiClient(HttpClient httpClient)
{
    public async Task<DailyFarmingPlanViewModel> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/investments/players/{allyCode}/daily-farming-plan",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DailyFarmingPlanViewModel>(cancellationToken)
            ?? throw new InvalidOperationException("Daily farming plan API returned an empty response.");
    }

    public enum DailyFarmingChannelViewModel
    {
        CantinaEnergy = 1,
        NormalEnergy = 2,
        FleetEnergy = 3,
        Scavenger = 4,
        Stores = 5,
        Credits = 6
    }

    public enum DailyFarmingPrecisionViewModel
    {
        Exact = 1,
        Guided = 2,
        Check = 3
    }

    public sealed record EnergyBaselineViewModel(
        DailyFarmingChannelViewModel Channel,
        string Label,
        int RegenerationPerDay,
        int BonusEnergyPerDay,
        int BaselineFreeEnergy,
        string Note);

    public sealed record ActionViewModel(
        int Rank,
        DailyFarmingChannelViewModel Channel,
        string ChannelLabel,
        DailyFarmingPrecisionViewModel Precision,
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

    public sealed record DailyFarmingPlanViewModel(
        long AllyCode,
        DateTimeOffset GeneratedAtUtc,
        bool HasInventorySnapshot,
        DateTimeOffset? InventoryCapturedAtUtc,
        int ActiveTargetCount,
        int MissingResourceTypes,
        IReadOnlyCollection<EnergyBaselineViewModel> EnergyBaselines,
        IReadOnlyCollection<ActionViewModel> Actions,
        string Summary,
        string Limitation);
}
