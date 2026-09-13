using System.Net;
using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class ConquestApiClient(HttpClient httpClient)
{
    public async Task<PlanViewModel?> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/conquest/players/{allyCode}/current",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlanViewModel>(cancellationToken);
    }

    public async Task<PlanViewModel> SaveCurrentAsync(
        long allyCode,
        SavePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/v1/conquest/players/{allyCode}/current",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlanViewModel>(cancellationToken)
            ?? throw new InvalidOperationException("The Conquest API returned an empty plan response.");
    }

    public async Task<OptimizationViewModel?> OptimizeCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/api/v1/conquest/players/{allyCode}/current/optimize",
            content: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OptimizationViewModel>(cancellationToken);
    }

    public sealed record PlanViewModel(
        string Id,
        long AllyCode,
        string EventId,
        string Name,
        string Difficulty,
        IReadOnlyCollection<FeatViewModel> Feats,
        int CompletedFeats,
        int TotalFeats,
        int EarnedFeatPoints,
        int AvailableFeatPoints,
        int StaminaCostPerBattle,
        int ReserveFloorPercent,
        IReadOnlyCollection<UnitStaminaViewModel> Stamina,
        DateTimeOffset UpdatedAtUtc);

    public sealed record UnitStaminaViewModel(string DefinitionId, int CurrentPercent);

    public sealed record FeatViewModel(
        Guid Id,
        string Name,
        string Scope,
        int? Sector,
        int Points,
        int Target,
        int Progress,
        int Remaining,
        int ExpectedProgressPerBattle,
        string RuleType,
        string? Faction,
        IReadOnlyCollection<string> UnitDefinitionIds,
        int MinimumMatchingUnits,
        bool IsComplete);

    public sealed record OptimizationViewModel(
        long AllyCode,
        string EventId,
        int PendingFeats,
        int CandidateCharacters,
        int StaminaCostPerBattle,
        int ReserveFloorPercent,
        IReadOnlyCollection<TeamViewModel> Recommendations,
        IReadOnlyCollection<Guid> UncoveredFeatIds);

    public sealed record TeamViewModel(
        int Rank,
        decimal Score,
        decimal FeatEfficiency,
        long TeamGalacticPower,
        decimal? AverageSpeed,
        decimal AverageStamina,
        decimal ExpectedPostBattleAverageStamina,
        decimal StaminaOpportunityCost,
        int ReserveRiskUnits,
        IReadOnlyCollection<UnitViewModel> Team,
        IReadOnlyCollection<ContributionViewModel> AdvancesFeats,
        string Rationale);

    public sealed record UnitViewModel(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        int RelicTier,
        long GalacticPower,
        decimal? Speed,
        IReadOnlyCollection<string> Factions,
        int CurrentStamina,
        int ExpectedPostBattleStamina,
        bool BelowReserveAfterBattle);

    public sealed record ContributionViewModel(
        Guid FeatId,
        string FeatName,
        int Points,
        int Remaining,
        int ExpectedProgress,
        decimal PointValueThisBattle);

    public sealed record SavePlanRequest(
        string EventId,
        string Name,
        string Difficulty,
        IReadOnlyCollection<SaveFeatRequest> Feats,
        int StaminaCostPerBattle,
        int ReserveFloorPercent,
        IReadOnlyCollection<SaveUnitStaminaRequest> Stamina);

    public sealed record SaveUnitStaminaRequest(string DefinitionId, int CurrentPercent);

    public sealed record SaveFeatRequest(
        Guid? Id,
        string Name,
        string Scope,
        int? Sector,
        int Points,
        int Target,
        int Progress,
        int ExpectedProgressPerBattle,
        string RuleType,
        string? Faction,
        IReadOnlyCollection<string> UnitDefinitionIds,
        int MinimumMatchingUnits);
}
