using System.Net;
using System.Net.Http.Json;

namespace Swgoh.Blazor.Clients;

public sealed class GacPlannerApiClient(HttpClient httpClient)
{
    public async Task<PlannerResult> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/gac/players/{allyCode}/planner/current",
            cancellationToken);
        return await ReadPlannerResultAsync(response, cancellationToken);
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

    public sealed record PlannerUnavailableViewModel(string Status, string? Message);

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
        DateTimeOffset UpdatedAtUtc);

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
        string? Notes);

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
        IReadOnlyCollection<SaveAttackRequest> Attacks);

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
}
