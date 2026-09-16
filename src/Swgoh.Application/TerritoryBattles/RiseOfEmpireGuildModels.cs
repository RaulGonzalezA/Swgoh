namespace Swgoh.Application.TerritoryBattles;

public sealed record RiseOfEmpireGuildAnalysis(
    string GuildId,
    string GuildName,
    long GuildGalacticPower,
    int DetectedMembers,
    int ImportedMembers,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyCollection<string> Warnings,
    IReadOnlyCollection<RiseOfEmpireGuildPhasePlan> Phases,
    IReadOnlyCollection<RiseOfEmpireOperationPlan> Operations,
    IReadOnlyCollection<RiseOfEmpireBonusUnlockReadiness> BonusUnlocks,
    IReadOnlyCollection<RiseOfEmpireGuildMissionCoverage> MissionCoverage,
    IReadOnlyCollection<RiseOfEmpireGuildUpgradePriority> UpgradePriorities)
{
    public int ProjectedStars => Phases.Sum(phase => phase.ProjectedStars);
    public int OperationSlots => Operations.Sum(operation => operation.TotalSlots);
    public int FilledOperationSlots => Operations.Sum(operation => operation.FilledSlots);
    public int CoveredMissions => MissionCoverage.Count(mission => mission.PlannedMembers.Count > 0);
    public int MissionReadyMembers => MissionCoverage.Sum(mission => mission.ReadyMembers.Count);
    public int MissionAttemptTarget => MissionCoverage.Sum(mission => mission.TargetAttempts);
    public int PlannedMissionAttempts => MissionCoverage.Sum(mission => mission.PlannedMembers.Count);
    public int OverlapBlockedMissionAttempts => MissionCoverage.Sum(mission => mission.OverlapBlockedMembers.Count);
}

public sealed record RiseOfEmpireGuildPhasePlan(
    int Phase,
    long AvailableGalacticPower,
    long ForcedOperationDeploymentGalacticPower,
    int ProjectedStars,
    IReadOnlyCollection<RiseOfEmpireGuildPlanetPlan> Planets);

public sealed record RiseOfEmpireGuildPlanetPlan(
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

public sealed record RiseOfEmpireOperationPlan(
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
    IReadOnlyCollection<RiseOfEmpireOperationSquadPlan> Squads);

public sealed record RiseOfEmpireOperationSquadPlan(
    string Id,
    long Points,
    bool Complete,
    IReadOnlyCollection<RiseOfEmpireOperationAssignment> Assignments,
    IReadOnlyCollection<RiseOfEmpireOperationMissingSlot> MissingSlots);

public sealed record RiseOfEmpireOperationAssignment(
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

public sealed record RiseOfEmpireOperationMissingSlot(
    string BaseId,
    string UnitName,
    bool IsShip,
    int RequiredRarity,
    int RequiredRelicTier,
    IReadOnlyCollection<RiseOfEmpireNearCandidate> NearCandidates);

public sealed record RiseOfEmpireNearCandidate(
    long PlayerAllyCode,
    string PlayerName,
    int CurrentRarity,
    int CurrentRelicTier,
    int RelicsMissing);

public sealed record RiseOfEmpireBonusUnlockReadiness(
    string PlanetName,
    string SourcePlanet,
    int RequiredClears,
    int EligibleMembers,
    bool ProjectedUnlocked,
    IReadOnlyCollection<RiseOfEmpireGuildMemberReadiness> Eligible,
    IReadOnlyCollection<RiseOfEmpireGuildMemberReadiness> Closest);

public sealed record RiseOfEmpireGuildMemberReadiness(
    long AllyCode,
    string PlayerName,
    bool Ready,
    IReadOnlyCollection<string> MissingRequirements);

public sealed record RiseOfEmpireGuildMissionCoverage(
    int Phase,
    string PlanetId,
    string PlanetName,
    string MissionId,
    string MissionName,
    string Type,
    bool IsFleet,
    int TargetAttempts,
    int EligibleMembers,
    IReadOnlyCollection<RiseOfEmpireGuildMissionMember> ReadyMembers,
    IReadOnlyCollection<RiseOfEmpireGuildMissionMember> PlannedMembers,
    IReadOnlyCollection<RiseOfEmpireGuildMissionMember> OverlapBlockedMembers,
    IReadOnlyCollection<RiseOfEmpireGuildMissionMember> ClosestMembers)
{
    public int MissingPlannedAttempts => Math.Max(0, TargetAttempts - PlannedMembers.Count);
}

public sealed record RiseOfEmpireGuildMissionMember(
    long AllyCode,
    string PlayerName,
    string? TeamName,
    bool Ready,
    int ReadyUnits,
    int RequiredUnits,
    IReadOnlyCollection<string> MissingRequirements);

public sealed record RiseOfEmpireGuildUpgradePriority(
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
    IReadOnlyCollection<string> Reasons);

public sealed record RiseOfEmpireGuildSnapshot(
    string GuildId,
    string GuildName,
    long GalacticPower,
    int DetectedMembers,
    IReadOnlyCollection<RiseOfEmpireGuildMemberReference> Members,
    IReadOnlyCollection<string> Warnings);

public sealed record RiseOfEmpireGuildMemberReference(
    string PlayerId,
    long AllyCode,
    string PlayerName,
    long GalacticPower);
