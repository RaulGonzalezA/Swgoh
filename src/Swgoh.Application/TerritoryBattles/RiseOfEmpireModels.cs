namespace Swgoh.Application.TerritoryBattles;

public sealed record RiseOfEmpireAnalysis(
    long AllyCode,
    string PlayerName,
    DateTimeOffset RosterUpdatedAtUtc,
    string CatalogVersion,
    IReadOnlyCollection<RiseOfEmpirePhaseAnalysis> Phases,
    IReadOnlyCollection<RiseOfEmpireUpgradePriority> UpgradePriorities)
{
    public int ReadyTeams => Phases.SelectMany(phase => phase.Planets).Sum(planet => planet.ReadyTeamCount);
    public int ReadyPlanets => Phases.SelectMany(phase => phase.Planets).Count(planet => planet.ReadinessPercent >= 100m);
}

public sealed record RiseOfEmpirePhaseAnalysis(
    int Phase,
    int MinimumRelicTier,
    IReadOnlyCollection<RiseOfEmpirePlanetAnalysis> Planets);

public sealed record RiseOfEmpirePlanetAnalysis(
    string Id,
    string Name,
    string Alignment,
    int MinimumRelicTier,
    bool IsBonusZone,
    IReadOnlyCollection<long> StarThresholds,
    int EligibleCharacterCount,
    int ReadyTeamCount,
    decimal ReadinessPercent,
    IReadOnlyCollection<RiseOfEmpireTeamRecommendation> RecommendedTeams,
    IReadOnlyCollection<RiseOfEmpireMissionReadiness> Missions);

public sealed record RiseOfEmpireTeamRecommendation(
    string Archetype,
    string FactionKey,
    bool Ready,
    int ReadyUnits,
    int RequiredUnits,
    decimal Score,
    IReadOnlyCollection<RiseOfEmpireUnit> Team,
    IReadOnlyCollection<RiseOfEmpireUnit> NextUpgrades,
    string Rationale);

public sealed record RiseOfEmpireUnit(
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    int RelicTier,
    long GalacticPower,
    decimal? Speed,
    int RelicsMissing);

public sealed record RiseOfEmpireMissionReadiness(
    string Name,
    string Type,
    string Requirement,
    int MinimumRelicTier,
    bool Ready,
    IReadOnlyCollection<string> MissingRequirements);

public sealed record RiseOfEmpireUpgradePriority(
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
