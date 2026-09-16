namespace Swgoh.Blazor.Clients;

public sealed class RiseOfEmpireApiClient(HttpClient httpClient)
{
    public async Task<RiseOfEmpireAnalysisViewModel?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/territory-battles/players/{allyCode}/rise-of-the-empire/analysis",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiseOfEmpireAnalysisViewModel>(cancellationToken);
    }

    public sealed record RiseOfEmpireAnalysisViewModel(
        long AllyCode,
        string PlayerName,
        DateTimeOffset RosterUpdatedAtUtc,
        string CatalogVersion,
        IReadOnlyCollection<RiseOfEmpirePhaseViewModel> Phases,
        IReadOnlyCollection<RiseOfEmpireUpgradePriorityViewModel> UpgradePriorities,
        int ReadyTeams,
        int ReadyPlanets);

    public sealed record RiseOfEmpirePhaseViewModel(
        int Phase,
        int MinimumRelicTier,
        IReadOnlyCollection<RiseOfEmpirePlanetViewModel> Planets);

    public sealed record RiseOfEmpirePlanetViewModel(
        string Id,
        string Name,
        string Alignment,
        int MinimumRelicTier,
        bool IsBonusZone,
        IReadOnlyCollection<long> StarThresholds,
        int EligibleCharacterCount,
        int ReadyTeamCount,
        decimal ReadinessPercent,
        RiseOfEmpireMissionReadinessViewModel? AccessRequirement,
        IReadOnlyCollection<RiseOfEmpireTeamRecommendationViewModel> RecommendedTeams,
        IReadOnlyCollection<RiseOfEmpireMissionReadinessViewModel> Missions);

    public sealed record RiseOfEmpireTeamRecommendationViewModel(
        string Archetype,
        string FactionKey,
        bool Ready,
        int ReadyUnits,
        int RequiredUnits,
        decimal Score,
        IReadOnlyCollection<RiseOfEmpireUnitViewModel> Team,
        IReadOnlyCollection<RiseOfEmpireUnitViewModel> NextUpgrades,
        string Rationale);

    public sealed record RiseOfEmpireUnitViewModel(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        int RelicTier,
        long GalacticPower,
        decimal? Speed,
        int RelicsMissing);

    public sealed record RiseOfEmpireMissionReadinessViewModel(
        string Name,
        string Type,
        string Requirement,
        int MinimumRelicTier,
        bool Ready,
        IReadOnlyCollection<string> MissingRequirements);

    public sealed record RiseOfEmpireUpgradePriorityViewModel(
        int Rank,
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        int CurrentRelicTier,
        int TargetRelicTier,
        int RelicsMissing,
        int UnlockValue,
        decimal Score,
        IReadOnlyCollection<string> Planets,
        string Reason);
}
