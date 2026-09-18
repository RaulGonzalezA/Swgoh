using System.Globalization;
using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class DailyFarmingPlanApiClient(HttpClient httpClient)
{
    public Task<DailyFarmingPlanViewModel> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default) =>
        GetAsync(allyCode, 0, cancellationToken);

    public Task<DailyFarmingPlanViewModel> GetAsync(
        long allyCode,
        int dailyCrystalBudget,
        CancellationToken cancellationToken = default) =>
        GetAsync(
            allyCode,
            dailyCrystalBudget,
            new Dictionary<string, decimal>(StringComparer.Ordinal),
            cancellationToken);

    public async Task<DailyFarmingPlanViewModel> GetAsync(
        long allyCode,
        int dailyCrystalBudget,
        IReadOnlyDictionary<string, decimal> manualDailyRates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manualDailyRates);

        int budget = Math.Clamp(dailyCrystalBudget, 0, 5_000);
        string rates = string.Join(
            ",",
            manualDailyRates
                .Where(pair => pair.Value > 0m)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                    $"{pair.Key}:{pair.Value.ToString(CultureInfo.InvariantCulture)}"));
        string url = $"/api/v1/investments/players/{allyCode}/daily-farming-plan?crystalBudget={budget}";
        if (!string.IsNullOrEmpty(rates))
        {
            url += $"&dailyRates={Uri.EscapeDataString(rates)}";
        }

        using HttpResponseMessage response = await httpClient.GetAsync(
            url,
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

    public sealed record RefreshRecommendationViewModel(
        DailyFarmingChannelViewModel Channel,
        string ChannelLabel,
        int RefreshCount,
        int CrystalCost,
        int EnergyGained,
        int BaselineFreeEnergy,
        int PlannedDailyEnergy,
        int? NextRefreshCost,
        string Reason);

    public sealed record CrystalBudgetViewModel(
        int DailyCrystalBudget,
        int CrystalsSpent,
        int CrystalsUnspent,
        string ProfileLabel,
        IReadOnlyCollection<RefreshRecommendationViewModel> Refreshes,
        int RefreshCount,
        int EnergyGained);

    public enum DailyResourceEtaModeViewModel
    {
        EnergyFarm = 1,
        ManualCadence = 2
    }

    public sealed record ResourceEtaViewModel(
        string ResourceId,
        string ResourceName,
        long Missing,
        DailyResourceEtaModeViewModel Mode,
        decimal? ExpectedDropsPerAttempt,
        int? EnergyCostPerAttempt,
        int? PlannedDailyEnergy,
        decimal ExpectedDailyYield,
        int EstimatedDays,
        DateTimeOffset EstimatedCompletionAtUtc,
        string Basis);

    public sealed record ManualCadenceResourceViewModel(
        string ResourceId,
        string ResourceName,
        long Missing,
        decimal? DailyRate,
        int AffectedTargetCount,
        string Priority);

    public sealed record TargetEtaViewModel(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        bool ReadyNow,
        bool FullEstimateAvailable,
        int? EstimatedDays,
        DateTimeOffset? EstimatedCompletionAtUtc,
        int ModeledBlockingResourceTypes,
        int UnknownBlockingResourceTypes,
        DateTimeOffset? KnownBottleneckCompletionAtUtc,
        string Summary);

    public sealed record BudgetScenarioTargetViewModel(
        string DefinitionId,
        string Name,
        bool FullEstimateAvailable,
        int? EstimatedDays,
        int? DaysSavedVsF2P);

    public sealed record BudgetScenarioViewModel(
        int DailyCrystalBudget,
        string ProfileLabel,
        bool IsCurrent,
        int CrystalsSpent,
        int CrystalsUnspent,
        int RefreshCount,
        int EnergyGained,
        int FullyEstimatedTargetCount,
        int ReadyNowTargetCount,
        int? ModeledPortfolioDays,
        DateTimeOffset? ModeledPortfolioCompletionAtUtc,
        int? DaysSavedVsF2P,
        IReadOnlyCollection<BudgetScenarioTargetViewModel> Targets);

    public sealed record DailyFarmingPlanViewModel(
        long AllyCode,
        DateTimeOffset GeneratedAtUtc,
        bool HasInventorySnapshot,
        DateTimeOffset? InventoryCapturedAtUtc,
        int ActiveTargetCount,
        int MissingResourceTypes,
        IReadOnlyCollection<EnergyBaselineViewModel> EnergyBaselines,
        IReadOnlyCollection<ActionViewModel> Actions,
        CrystalBudgetViewModel CrystalBudget,
        IReadOnlyCollection<ResourceEtaViewModel> ResourceEtas,
        IReadOnlyCollection<TargetEtaViewModel> TargetEtas,
        IReadOnlyCollection<ManualCadenceResourceViewModel> ManualCadenceResources,
        IReadOnlyCollection<BudgetScenarioViewModel> BudgetScenarios,
        string Summary,
        string Limitation,
        int FullyEstimatedTargetCount,
        int ReadyNowTargetCount);
}
