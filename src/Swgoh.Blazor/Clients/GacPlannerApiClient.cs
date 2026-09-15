using System.Net;
using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class GacPlannerApiClient(HttpClient httpClient)
{
    public async Task<PlannerResult> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < 210; attempt++)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                $"/api/v1/gac/players/{allyCode}/planner/current", cancellationToken);
            if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.Conflict)
            {
                var status = await response.Content.ReadFromJsonAsync<PlannerUnavailableViewModel>(cancellationToken);
                if (status?.Status == "Pending")
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                return new PlannerResult(null, status?.Message ?? "No hay una ronda de Gran Arena disponible.");
            }

            return await ReadPlannerResultAsync(response, cancellationToken);
        }

        return new PlannerResult(null, "La búsqueda continúa en segundo plano. Puedes volver a consultar más tarde.");
    }

    public async Task<PlannerResult> SaveCurrentAsync(
        long allyCode,
        SavePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/v1/gac/players/{allyCode}/planner/current",
            request,
            cancellationToken);
        return await ReadPlannerResultAsync(response, cancellationToken);
    }

    public async Task<OptimizationResult> OptimizeCurrentAsync(
        long allyCode,
        string mode,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/v1/gac/players/{allyCode}/planner/current/optimize",
            new OptimizeRequest(mode, apply),
            cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            OptimizationEnvelopeViewModel? envelope =
                await response.Content.ReadFromJsonAsync<OptimizationEnvelopeViewModel>(cancellationToken);
            return new OptimizationResult(envelope, null);
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            PlannerUnavailableViewModel? unavailable =
                await response.Content.ReadFromJsonAsync<PlannerUnavailableViewModel>(cancellationToken);
            return new OptimizationResult(null, unavailable?.Message ?? "No hay una ronda de Gran Arena disponible.");
        }

        response.EnsureSuccessStatusCode();
        return new OptimizationResult(null, "No se ha podido optimizar la ronda.");
    }

    public async Task<TeamPresetViewModel> CreatePresetAsync(
        long allyCode,
        SavePresetRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/v1/gac/players/{allyCode}/planner/presets",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TeamPresetViewModel>(cancellationToken)
            ?? throw new InvalidOperationException("The planner API returned an empty team preset response.");
    }

    public async Task<TeamPresetViewModel?> UpdatePresetAsync(
        long allyCode,
        Guid id,
        SavePresetRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/v1/gac/players/{allyCode}/planner/presets/{id}",
            request,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TeamPresetViewModel>(cancellationToken);
    }

    public async Task DeletePresetAsync(
        long allyCode,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.DeleteAsync(
            $"/api/v1/gac/players/{allyCode}/planner/presets/{id}",
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<PlannerResult> ReadPlannerResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            PlannerViewModel? planner = await response.Content.ReadFromJsonAsync<PlannerViewModel>(cancellationToken);
            return new PlannerResult(planner, null);
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            PlannerUnavailableViewModel? unavailable =
                await response.Content.ReadFromJsonAsync<PlannerUnavailableViewModel>(cancellationToken);
            return new PlannerResult(null, unavailable?.Message ?? "No hay una ronda de Gran Arena disponible.");
        }

        response.EnsureSuccessStatusCode();
        return new PlannerResult(null, "No se ha podido consultar el planificador.");
    }

    public sealed record PlannerResult(PlannerViewModel? Planner, string? Message);

    public sealed record OptimizationResult(OptimizationEnvelopeViewModel? Envelope, string? Message);

    public sealed record PlannerUnavailableViewModel(string Status, string? Message);

    public sealed record OptimizationEnvelopeViewModel(
        PlannerViewModel Planner,
        OptimizationViewModel Optimization);

    public sealed record OptimizationViewModel(
        string Mode,
        bool Applied,
        int TargetDefenses,
        int RecommendedAttacks,
        int HistoricalMatches,
        decimal AverageScore,
        decimal? KnownAverageBanners,
        IReadOnlyCollection<Guid> UncoveredDefenseIds,
        IReadOnlyCollection<OptimizationRecommendationViewModel> Recommendations,
        bool SearchLimitReached,
        IReadOnlyCollection<CounterDefenseAnalysisViewModel> CounterAnalyses);

    public sealed record CounterDefenseAnalysisViewModel(
        Guid DefenseId,
        string DefenseName,
        string Zone,
        IReadOnlyCollection<CounterCandidateAnalysisViewModel> Candidates);

    public sealed record CounterCandidateAnalysisViewModel(
        int Rank,
        Guid TeamPresetId,
        string TeamName,
        decimal Score,
        decimal EstimatedWinProbability,
        decimal? ExpectedBanners,
        string Risk,
        string TimeoutRisk,
        decimal StrategicCost,
        decimal CriticalPieceCost,
        string Evidence,
        string Confidence,
        string Rationale,
        string DatacronStatus,
        decimal TacticalAdjustment,
        decimal PersonalAdjustment,
        int FutureDefensesAtRisk);

    public sealed record OptimizationRecommendationViewModel(
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
        string PersonalRationale,
        decimal EstimatedWinProbability,
        string Risk,
        string TimeoutRisk,
        decimal CriticalPieceCost);

    public sealed record PlannerViewModel(
        PlannerOpponentViewModel Opponent,
        IReadOnlyCollection<TeamPresetViewModel> Presets,
        RoundPlanViewModel Plan);

    public sealed record PlannerOpponentViewModel(
        long OpponentAllyCode,
        string OpponentName,
        string League,
        string Format,
        int? RoundNumber);

    public sealed record RoundPlanViewModel(
        string Id,
        long PlayerAllyCode,
        long OpponentAllyCode,
        string OpponentName,
        string EventId,
        string EventInstanceId,
        int RoundNumber,
        string Format,
        string League,
        IReadOnlyCollection<OwnDefenseViewModel> OwnDefenses,
        IReadOnlyCollection<VisibleDefenseViewModel> VisibleDefenses,
        IReadOnlyCollection<AttackViewModel> Attacks,
        IReadOnlyCollection<ConflictViewModel> Conflicts,
        IReadOnlyCollection<CounterHintViewModel> CounterHints,
        DateTimeOffset UpdatedAtUtc,
        long Version);

    public sealed record OwnDefenseViewModel(Guid Id, string Zone, TeamPresetViewModel Team);

    public sealed record VisibleDefenseViewModel(
        Guid Id,
        string Zone,
        string? Label,
        PlannerSquadViewModel Squad,
        bool Defeated);

    public sealed record AttackViewModel(
        Guid Id,
        Guid DefenseId,
        TeamPresetViewModel Team,
        int Attempt,
        string Status,
        string? Notes,
        int? Banners);

    public sealed record ConflictViewModel(
        string Code,
        string Severity,
        string Message,
        IReadOnlyCollection<Guid> RelatedAssignmentIds,
        IReadOnlyCollection<string> UnitDefinitionIds);

    public sealed record CounterHintViewModel(
        Guid DefenseId,
        string ThreatName,
        string Confidence,
        string Source,
        string Rationale,
        bool RequiresDatacronVerification,
        Guid? MatchingTeamPresetId,
        IReadOnlyCollection<PlannerUnitViewModel> RecommendedTeam,
        int? Uses,
        decimal? WinRate,
        decimal? OneShotRate,
        decimal? AverageBanners,
        int? PlayersObserved);

    public sealed record TeamPresetViewModel(
        Guid Id,
        long AllyCode,
        string Name,
        string Format,
        string Use,
        PlannerSquadViewModel Squad,
        DateTimeOffset UpdatedAtUtc);

    public sealed record PlannerSquadViewModel(
        PlannerUnitViewModel Leader,
        IReadOnlyCollection<PlannerUnitViewModel> Members,
        bool IsFleet)
    {
        public IReadOnlyCollection<PlannerUnitViewModel> AllUnits => [Leader, .. Members];
    }

    public sealed record PlannerUnitViewModel(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        bool IsShip,
        long? GalacticPower,
        int? RelicTier,
        int? ZetaCount,
        int? OmicronCount);

    public sealed record SavePresetRequest(
        string Name,
        string Format,
        string Use,
        string LeaderDefinitionId,
        IReadOnlyCollection<string> MemberDefinitionIds,
        bool IsFleet);

    public sealed record SavePlanRequest(
        IReadOnlyCollection<SaveOwnDefenseRequest> OwnDefenses,
        IReadOnlyCollection<SaveVisibleDefenseRequest> VisibleDefenses,
        IReadOnlyCollection<SaveAttackRequest> Attacks,
        long ExpectedVersion);

    public sealed record SaveOwnDefenseRequest(Guid? Id, string Zone, Guid TeamPresetId);

    public sealed record SaveVisibleDefenseRequest(
        Guid? Id,
        string Zone,
        string? Label,
        string LeaderDefinitionId,
        IReadOnlyCollection<string> MemberDefinitionIds,
        bool IsFleet);

    public sealed record SaveAttackRequest(
        Guid? Id,
        Guid DefenseId,
        Guid TeamPresetId,
        int Attempt,
        string Status,
        string? Notes);

    public sealed record OptimizeRequest(string Mode, bool Apply);
}
