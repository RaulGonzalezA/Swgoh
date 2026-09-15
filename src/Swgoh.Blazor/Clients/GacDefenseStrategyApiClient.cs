using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class GacDefenseStrategyApiClient(HttpClient httpClient)
{
    public async Task<StrategyViewModel?> GetAsync(
        long allyCode,
        string format,
        CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<StrategyViewModel>(
            $"/api/v1/gac/players/{allyCode}/planner/strategy?format={Uri.EscapeDataString(format)}",
            cancellationToken);

    public async Task<StrategyViewModel?> SaveAsync(
        long allyCode,
        SaveStrategyRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/v1/gac/players/{allyCode}/planner/strategy",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StrategyViewModel>(cancellationToken);
    }

    public async Task<GenerationViewModel?> GenerateAsync(
        long allyCode,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/v1/gac/players/{allyCode}/planner/strategy/generate-defense",
            new GenerateDefenseRequest(apply),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GenerationViewModel>(cancellationToken);
    }

    public async Task<SmartGenerationViewModel?> GenerateSmartAsync(
        long allyCode,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/v1/gac/players/{allyCode}/planner/strategy/generate-smart-defense",
            new GenerateDefenseRequest(apply),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SmartGenerationViewModel>(cancellationToken);
    }

    public sealed record StrategyViewModel(
        string Format,
        IReadOnlyCollection<StrategySlotViewModel> Slots,
        IReadOnlyCollection<Guid> ReservedAttackPresetIds,
        IReadOnlyCollection<StrategyPresetViewModel> Presets,
        DateTimeOffset UpdatedAtUtc);

    public sealed record StrategySlotViewModel(
        int Position,
        string Zone,
        Guid? PinnedTeamPresetId);

    public sealed record StrategyPresetViewModel(
        Guid Id,
        string Name,
        string Use,
        bool IsFleet);

    public sealed record SaveStrategyRequest(
        string Format,
        IReadOnlyCollection<StrategySlotViewModel> Slots,
        IReadOnlyCollection<Guid> ReservedAttackPresetIds);

    public sealed record GenerationViewModel(
        string Format,
        bool Applied,
        IReadOnlyCollection<GeneratedAssignmentViewModel> Assignments,
        IReadOnlyCollection<string> Warnings,
        DateTimeOffset? PlanUpdatedAtUtc);

    public sealed record GeneratedAssignmentViewModel(
        int Position,
        string Zone,
        Guid TeamPresetId,
        string TeamName,
        bool Pinned,
        bool IsFleet,
        long GalacticPower);

    public sealed record SmartGenerationViewModel(
        string Format,
        bool Applied,
        string IntelligenceMode,
        int OpponentRoundsAnalyzed,
        decimal? OpponentFullClearRate,
        IReadOnlyCollection<SmartGeneratedAssignmentViewModel> Assignments,
        IReadOnlyCollection<string> Warnings,
        DateTimeOffset? PlanUpdatedAtUtc);

    public sealed record SmartGeneratedAssignmentViewModel(
        int Position,
        string Zone,
        Guid TeamPresetId,
        string TeamName,
        bool Pinned,
        bool IsFleet,
        long GalacticPower,
        decimal Score,
        decimal DefensiveValue,
        decimal OffensiveOpportunityCost,
        string Confidence,
        bool ContainsGalacticLegend,
        int OmicronCount,
        int EligibleDatacronTier,
        int OpponentSamples,
        int PersonalSamples,
        IReadOnlyCollection<string> Reasons);

    private sealed record GenerateDefenseRequest(bool Apply);
}
