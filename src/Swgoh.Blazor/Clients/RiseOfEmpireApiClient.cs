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

    public Task<RiseOfEmpireGuildAnalysisViewModel?> GetGuildAsync(
        long allyCode,
        CancellationToken cancellationToken = default) =>
        GetGuildCoreAsync(allyCode, sync: false, cancellationToken);

    public Task<RiseOfEmpireGuildAnalysisViewModel?> SyncGuildAsync(
        long allyCode,
        CancellationToken cancellationToken = default) =>
        GetGuildCoreAsync(allyCode, sync: true, cancellationToken);

    private async Task<RiseOfEmpireGuildAnalysisViewModel?> GetGuildCoreAsync(
        long allyCode,
        bool sync,
        CancellationToken cancellationToken)
    {
        string url = $"/api/v1/territory-battles/players/{allyCode}/rise-of-the-empire/guild";
        using HttpResponseMessage response = sync
            ? await httpClient.PostAsync($"{url}/sync", content: null, cancellationToken)
            : await httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiseOfEmpireGuildAnalysisViewModel>(cancellationToken);
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

    public sealed record RiseOfEmpireGuildAnalysisViewModel(
        string GuildId,
        string GuildName,
        long GuildGalacticPower,
        int DetectedMembers,
        int ImportedMembers,
        DateTimeOffset GeneratedAtUtc,
        IReadOnlyCollection<string> Warnings,
        IReadOnlyCollection<RiseOfEmpireGuildPhasePlanViewModel> Phases,
        IReadOnlyCollection<RiseOfEmpireOperationPlanViewModel> Operations,
        IReadOnlyCollection<RiseOfEmpireBonusUnlockViewModel> BonusUnlocks,
        IReadOnlyCollection<RiseOfEmpireGuildUpgradePriorityViewModel> UpgradePriorities,
        int ProjectedStars,
        int OperationSlots,
        int FilledOperationSlots);

    public sealed record RiseOfEmpireGuildPhasePlanViewModel(
        int Phase,
        long AvailableGalacticPower,
        long ForcedOperationDeploymentGalacticPower,
        int ProjectedStars,
        IReadOnlyCollection<RiseOfEmpireGuildPlanetPlanViewModel> Planets);

    public sealed record RiseOfEmpireGuildPlanetPlanViewModel(
        string PlanetId,
        string PlanetName,
        bool IsBonusZone,
        bool Available,
        int TargetStars,
        long StarThreshold,
        long CompletedOperationPoints,
        long ForcedOperationDeploymentGalacticPower,
        long AdditionalDeploymentGalacticPower,
        string Reason);

    public sealed record RiseOfEmpireOperationPlanViewModel(
        string Id,
        int Phase,
        string PlanetName,
        string Type,
        bool IsBonus,
        long TotalPoints,
        int TotalSlots,
        int FilledSlots,
        long CompletedPoints,
        long ForcedDeploymentGalacticPower,
        IReadOnlyCollection<RiseOfEmpireOperationSquadPlanViewModel> Squads);

    public sealed record RiseOfEmpireOperationSquadPlanViewModel(
        string Id,
        long Points,
        bool Complete,
        IReadOnlyCollection<RiseOfEmpireOperationAssignmentViewModel> Assignments,
        IReadOnlyCollection<RiseOfEmpireOperationMissingSlotViewModel> MissingSlots);

    public sealed record RiseOfEmpireOperationAssignmentViewModel(
        string BaseId,
        string UnitName,
        bool IsShip,
        int RequiredRelicTier,
        long PlayerAllyCode,
        string PlayerName,
        int CurrentRelicTier,
        long UnitGalacticPower,
        int CombatCriticality,
        string AssignmentReason);

    public sealed record RiseOfEmpireOperationMissingSlotViewModel(
        string BaseId,
        string UnitName,
        bool IsShip,
        int RequiredRarity,
        int RequiredRelicTier,
        IReadOnlyCollection<RiseOfEmpireNearCandidateViewModel> NearCandidates);

    public sealed record RiseOfEmpireNearCandidateViewModel(
        long PlayerAllyCode,
        string PlayerName,
        int CurrentRarity,
        int CurrentRelicTier,
        int RelicsMissing);

    public sealed record RiseOfEmpireBonusUnlockViewModel(
        string PlanetName,
        string SourcePlanet,
        int RequiredClears,
        int EligibleMembers,
        bool ProjectedUnlocked,
        IReadOnlyCollection<RiseOfEmpireGuildMemberReadinessViewModel> Eligible,
        IReadOnlyCollection<RiseOfEmpireGuildMemberReadinessViewModel> Closest);

    public sealed record RiseOfEmpireGuildMemberReadinessViewModel(
        long AllyCode,
        string PlayerName,
        bool Ready,
        IReadOnlyCollection<string> MissingRequirements);

    public sealed record RiseOfEmpireGuildUpgradePriorityViewModel(
        int Rank,
        long PlayerAllyCode,
        string PlayerName,
        string DefinitionId,
        string UnitName,
        int CurrentRelicTier,
        int TargetRelicTier,
        int RelicsMissing,
        decimal Score,
        IReadOnlyCollection<string> Reasons)
    {
        public string ReasonSummary => string.Join(" · ", Reasons.Take(2));
    }
}
