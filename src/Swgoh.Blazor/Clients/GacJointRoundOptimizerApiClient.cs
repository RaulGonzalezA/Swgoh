using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class GacJointRoundOptimizerApiClient(HttpClient httpClient)
{
    public async Task<JointOptimizationViewModel?> OptimizeAsync(
        long allyCode,
        string mode,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/v1/gac/players/{allyCode}/planner/current/optimize-round",
            new JointOptimizeRequest(mode, apply),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        JointOptimizationEnvelope? envelope = await response.Content
            .ReadFromJsonAsync<JointOptimizationEnvelope>(cancellationToken);
        return envelope?.Optimization;
    }

    public sealed record JointOptimizationEnvelope(
        object? Planner,
        JointOptimizationViewModel Optimization);

    public sealed record JointOptimizationViewModel(
        string Format,
        string Mode,
        bool Applied,
        int ScenariosEvaluated,
        JointScenarioViewModel Selected,
        IReadOnlyCollection<JointScenarioViewModel> Alternatives,
        DateTimeOffset? PlanUpdatedAtUtc,
        IReadOnlyCollection<string> Warnings);

    public sealed record JointScenarioViewModel(
        string ScenarioId,
        decimal JointScore,
        decimal DefenseScore,
        decimal DefenseCompletionRate,
        decimal AttackCoverageRate,
        decimal AttackScore,
        decimal? KnownAverageBanners,
        decimal OffensePreservationScore,
        decimal AverageDefenseOpportunityCost,
        int RecommendedAttacks,
        int TargetDefenses,
        int HistoricalMatches,
        bool AttackSearchLimitReached,
        IReadOnlyCollection<JointDefenseAssignmentViewModel> DefenseAssignments,
        IReadOnlyCollection<AttackRecommendationViewModel> AttackRecommendations,
        IReadOnlyCollection<Guid> UncoveredDefenseIds,
        IReadOnlyCollection<string> Warnings);

    public sealed record JointDefenseAssignmentViewModel(
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

    public sealed record AttackRecommendationViewModel(
        Guid DefenseId,
        string DefenseName,
        string Zone,
        Guid TeamPresetId,
        string TeamName,
        decimal Score,
        decimal StrategicCost,
        string Evidence,
        string Confidence,
        string Rationale,
        decimal? WinRate,
        decimal? OneShotRate,
        decimal? AverageBanners,
        int? Uses,
        decimal TacticalAdjustment,
        decimal? TeamAverageSpeed,
        decimal? DefenseAverageSpeed,
        decimal? TeamModSpeedBonus,
        decimal? DefenseModSpeedBonus,
        string DatacronStatus,
        decimal BaseStrategicCost,
        decimal OpportunityCost,
        int StrategicAlternatives,
        int FutureDefensesAtRisk,
        string StrategicRationale,
        decimal PersonalAdjustment,
        int PersonalSamples,
        int PersonalWins,
        decimal? PersonalWinRate,
        decimal? PersonalOneShotRate,
        decimal? PersonalAverageBanners,
        string PersonalScope,
        string PersonalRationale);

    private sealed record JointOptimizeRequest(string Mode, bool Apply);
}
