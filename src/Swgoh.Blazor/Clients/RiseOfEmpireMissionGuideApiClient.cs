namespace Swgoh.Blazor.Clients;

public sealed class RiseOfEmpireMissionGuideApiClient(HttpClient httpClient)
{
    public async Task<MissionGuideAnalysisViewModel?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/territory-battles/players/{allyCode}/rise-of-the-empire/mission-guides",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MissionGuideAnalysisViewModel>(cancellationToken);
    }

    public sealed record MissionGuideAnalysisViewModel(
        long AllyCode,
        string PlayerName,
        DateTimeOffset RosterUpdatedAtUtc,
        string CatalogVersion,
        IReadOnlyCollection<PlanetMissionGuidesViewModel> Planets,
        int ReadyTeams,
        int ReadyFleetTeams);

    public sealed record PlanetMissionGuidesViewModel(
        string PlanetId,
        string PlanetName,
        int Phase,
        IReadOnlyCollection<MissionGuideViewModel> Missions);

    public sealed record MissionGuideViewModel(
        string Id,
        string Name,
        string Type,
        string Requirement,
        bool IsFleet,
        int MinimumRelicTier,
        bool Eligible,
        IReadOnlyCollection<string> MissingRequirements,
        IReadOnlyCollection<ConcreteTeamViewModel> RecommendedTeams,
        int ReadyTeamCount);

    public sealed record ConcreteTeamViewModel(
        string Name,
        string Confidence,
        bool Ready,
        int ReadyUnits,
        int RequiredUnits,
        IReadOnlyCollection<GuideUnitViewModel> Units,
        IReadOnlyCollection<string> MissingUnits,
        string Rationale);

    public sealed record GuideUnitViewModel(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        bool IsShip,
        int Rarity,
        int RelicTier,
        long GalacticPower,
        bool Ready,
        string Requirement);
}
