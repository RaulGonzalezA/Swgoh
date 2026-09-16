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

    public async Task<RiseOfEmpireGuildAnalysisViewModel?> GetGuildAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/territory-battles/players/{allyCode}/rise-of-the-empire/guild",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiseOfEmpireGuildAnalysisViewModel>(cancellationToken);
    }

    public async Task<RiseOfEmpireGuildSyncJobViewModel> StartGuildSyncAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/api/v1/territory-battles/players/{allyCode}/rise-of-the-empire/guild/sync",
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiseOfEmpireGuildSyncJobViewModel>(cancellationToken)
            ?? throw new HttpRequestException("The guild synchronization response was empty.");
    }

    public async Task<RiseOfEmpireGuildSyncJobViewModel?> GetLatestGuildSyncAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/territory-battles/players/{allyCode}/rise-of-the-empire/guild/sync",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiseOfEmpireGuildSyncJobViewModel>(cancellationToken);
    }

    public async Task<RiseOfEmpireGuildSyncJobViewModel?> GetGuildSyncAsync(
        long allyCode,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/territory-battles/players/{allyCode}/rise-of-the-empire/guild/sync/{Uri.EscapeDataString(jobId)}",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiseOfEmpireGuildSyncJobViewModel>(cancellationToken);
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
        IReadOnlyCollection<RiseOfEmpireGuildMissionCoverageViewModel> MissionCoverage,
        IReadOnlyCollection<RiseOfEmpireGuildUpgradePriorityViewModel> UpgradePriorities,
        int ProjectedStars,
        int OperationSlots,
        int FilledOperationSlots,
        int CoveredMissions,
        int MissionReadyMembers,
        int MissionAttemptTarget,
        int PlannedMissionAttempts,
        int OverlapBlockedMissionAttempts);

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
        int? NextStar,
        long? NextStarThreshold,
        long? NextStarGap,
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

    public sealed record RiseOfEmpireGuildMissionCoverageViewModel(
        int Phase,
        string PlanetId,
        string PlanetName,
        string MissionId,
        string MissionName,
        string Type,
        bool IsFleet,
        int TargetAttempts,
        int EligibleMembers,
        IReadOnlyCollection<RiseOfEmpireGuildMissionMemberViewModel> ReadyMembers,
        IReadOnlyCollection<RiseOfEmpireGuildMissionMemberViewModel> PlannedMembers,
        IReadOnlyCollection<RiseOfEmpireGuildMissionMemberViewModel> OverlapBlockedMembers,
        IReadOnlyCollection<RiseOfEmpireGuildMissionMemberViewModel> ClosestMembers,
        int MissingPlannedAttempts);

    public sealed record RiseOfEmpireGuildMissionMemberViewModel(
        long AllyCode,
        string PlayerName,
        string? TeamName,
        bool Ready,
        int ReadyUnits,
        int RequiredUnits,
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
        int MissionTeamsUnlocked,
        long? ClosestNextStarGap,
        IReadOnlyCollection<string> AffectedPlanets,
        IReadOnlyCollection<string> MissionNames,
        IReadOnlyCollection<string> Reasons)
    {
        public string ReasonSummary => string.Join(" · ", Reasons.Take(2));
    }

    public enum RiseOfEmpireGuildSyncStatusViewModel
    {
        Queued = 0,
        DiscoveringMembers = 1,
        RefreshingMembers = 2,
        BuildingPlan = 3,
        Completed = 4,
        Failed = 5
    }

    public sealed record RiseOfEmpireGuildSyncJobViewModel(
        string Id,
        long AllyCode,
        RiseOfEmpireGuildSyncStatusViewModel Status,
        int TotalMembers,
        int CompletedMembers,
        int FailedMembers,
        string? CurrentMember,
        string? GuildId,
        string? GuildName,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        string? Error)
    {
        public bool IsTerminal => Status is RiseOfEmpireGuildSyncStatusViewModel.Completed or RiseOfEmpireGuildSyncStatusViewModel.Failed;

        public int ProgressPercent => Status == RiseOfEmpireGuildSyncStatusViewModel.Completed
            ? 100
            : TotalMembers <= 0
                ? 0
                : Math.Clamp((int)Math.Round(CompletedMembers * 100m / TotalMembers), 0, 99);

        public string StatusLabel => Status switch
        {
            RiseOfEmpireGuildSyncStatusViewModel.Queued => "En cola",
            RiseOfEmpireGuildSyncStatusViewModel.DiscoveringMembers => "Detectando miembros",
            RiseOfEmpireGuildSyncStatusViewModel.RefreshingMembers => "Actualizando rosters",
            RiseOfEmpireGuildSyncStatusViewModel.BuildingPlan => "Calculando plan RotE",
            RiseOfEmpireGuildSyncStatusViewModel.Completed => "Completado",
            RiseOfEmpireGuildSyncStatusViewModel.Failed => "Error",
            _ => "Sincronizando"
        };
    }
}
