using System.Globalization;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireGuildMissionAnalyzer
{
    public static RiseOfEmpireGuildMissionPlanning Analyze(
        IReadOnlyCollection<PlayerProfile> players,
        GameDataCatalog gameData)
    {
        var coverage = new List<RiseOfEmpireGuildMissionCoverage>();
        var reservations = new RiseOfEmpireCombatReservationIndex();
        var upgrades = new List<RiseOfEmpireGuildMissionUpgradeCandidate>();

        foreach (RiseOfEmpirePlanetDefinition planet in RiseOfEmpireCatalog.Planets
                     .OrderBy(item => item.Phase)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!RiseOfEmpireMissionGuideCatalog.ByPlanet.TryGetValue(planet.Id, out IReadOnlyCollection<RiseOfEmpireMissionGuideDefinition>? missions))
            {
                continue;
            }

            foreach (RiseOfEmpireMissionGuideDefinition mission in missions)
            {
                var readyMembers = new List<RiseOfEmpireGuildMissionMember>();
                var closestMembers = new List<RiseOfEmpireGuildMissionMember>();
                int eligibleMembers = 0;

                foreach (PlayerProfile player in players)
                {
                    MissionEvaluation evaluation = Evaluate(player, mission, gameData);
                    if (evaluation.EntryEligible)
                    {
                        eligibleMembers++;
                    }

                    RiseOfEmpireGuildMissionMember member = ToMember(player, evaluation);
                    if (evaluation.Ready)
                    {
                        readyMembers.Add(member);
                    }
                    else if (evaluation.BestTeam is not null)
                    {
                        closestMembers.Add(member);
                    }

                    AddReservations(reservations, player, planet, mission, evaluation, gameData);
                    AddUpgradeCandidates(upgrades, player, planet, mission, evaluation, gameData);
                }

                coverage.Add(new RiseOfEmpireGuildMissionCoverage(
                    planet.Phase,
                    planet.Id,
                    planet.Name,
                    mission.Id,
                    mission.Name,
                    mission.Type,
                    mission.IsFleet,
                    eligibleMembers,
                    [.. readyMembers.OrderBy(member => member.PlayerName, StringComparer.OrdinalIgnoreCase)],
                    [.. closestMembers
                        .OrderBy(member => member.MissingRequirements.Count)
                        .ThenByDescending(member => member.ReadyUnits)
                        .ThenBy(member => member.PlayerName, StringComparer.OrdinalIgnoreCase)
                        .Take(8)]));
            }
        }

        return new RiseOfEmpireGuildMissionPlanning(coverage, reservations, upgrades);
    }

    private static MissionEvaluation Evaluate(
        PlayerProfile player,
        RiseOfEmpireMissionGuideDefinition mission,
        GameDataCatalog gameData)
    {
        RequirementEvaluation[] entry =
        [
            .. mission.RequiredUnits.Select(requirement => EvaluateRequirement(player, requirement, gameData))
        ];
        TeamEvaluation[] teams =
        [
            .. mission.RecommendedTeams
                .Select(team => EvaluateTeam(player, team, gameData))
                .OrderByDescending(team => team.Ready)
                .ThenByDescending(team => team.ReadyUnits)
                .ThenBy(team => ConfidenceOrder(team.Definition.Confidence))
        ];

        bool entryEligible = entry.All(item => item.Ready);
        TeamEvaluation? best = teams.FirstOrDefault();
        bool ready = entryEligible && best?.Ready == true;
        string[] missing =
        [
            .. entry.Where(item => !item.Ready).Select(item => item.MissingMessage),
            .. (best?.Requirements ?? []).Where(item => !item.Ready).Select(item => item.MissingMessage)
        ];
        return new MissionEvaluation(entryEligible, ready, entry, teams, best, [.. missing.Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    private static TeamEvaluation EvaluateTeam(
        PlayerProfile player,
        RiseOfEmpireConcreteTeamDefinition team,
        GameDataCatalog gameData)
    {
        RequirementEvaluation[] requirements =
        [
            .. team.Units.Select(requirement => EvaluateRequirement(player, requirement, gameData))
        ];
        return new TeamEvaluation(
            team,
            requirements.All(item => item.Ready),
            requirements.Count(item => item.Ready),
            requirements);
    }

    private static RequirementEvaluation EvaluateRequirement(
        PlayerProfile player,
        RiseOfEmpireGuideUnitDefinition requirement,
        GameDataCatalog gameData)
    {
        RosterUnit? unit = FindUnit(player, requirement, gameData);
        if (unit is null)
        {
            return new RequirementEvaluation(requirement, null, false, $"{requirement.Label}: no disponible");
        }

        if (unit.Rarity < requirement.MinimumRarity)
        {
            return new RequirementEvaluation(
                requirement,
                unit,
                false,
                $"{requirement.Label}: {unit.Rarity}★ → {requirement.MinimumRarity}★");
        }

        if (!requirement.IsShip && unit.RelicTier < requirement.MinimumRelicTier)
        {
            return new RequirementEvaluation(
                requirement,
                unit,
                false,
                $"{requirement.Label}: R{unit.RelicTier} → R{requirement.MinimumRelicTier}");
        }

        return new RequirementEvaluation(requirement, unit, true, string.Empty);
    }

    private static RiseOfEmpireGuildMissionMember ToMember(PlayerProfile player, MissionEvaluation evaluation) =>
        new(
            player.AllyCode,
            player.Name,
            evaluation.BestTeam?.Definition.Name,
            evaluation.Ready,
            evaluation.BestTeam?.ReadyUnits ?? 0,
            evaluation.BestTeam?.Requirements.Count ?? 0,
            evaluation.MissingRequirements);

    private static void AddReservations(
        RiseOfEmpireCombatReservationIndex reservations,
        PlayerProfile player,
        RiseOfEmpirePlanetDefinition planet,
        RiseOfEmpireMissionGuideDefinition mission,
        MissionEvaluation evaluation,
        GameDataCatalog gameData)
    {
        TeamEvaluation[] readyTeams = [.. evaluation.Teams.Where(team => team.Ready)];
        if (evaluation.EntryEligible && readyTeams.Length > 0)
        {
            var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (TeamEvaluation team in readyTeams)
            {
                foreach (RequirementEvaluation requirement in team.Requirements.Where(item => item.Unit is not null))
                {
                    occurrences[requirement.Unit!.DefinitionId] = occurrences.GetValueOrDefault(requirement.Unit.DefinitionId) + 1;
                }
            }

            foreach ((string definitionId, int count) in occurrences)
            {
                int criticality = count == readyTeams.Length ? 190 : 105;
                reservations.Reserve(
                    planet.Phase,
                    player.AllyCode,
                    definitionId,
                    criticality,
                    $"{mission.Name} en {planet.Name}: forma parte de {(count == readyTeams.Length ? "todas" : "una")} las composiciones listas.");
            }

            foreach (RequirementEvaluation required in evaluation.EntryRequirements.Where(item => item.Ready && item.Unit is not null))
            {
                reservations.Reserve(
                    planet.Phase,
                    player.AllyCode,
                    required.Unit!.DefinitionId,
                    220,
                    $"{mission.Name} en {planet.Name}: unidad obligatoria de entrada.");
            }

            return;
        }

        TeamEvaluation? best = evaluation.BestTeam;
        if (best is null || best.Requirements.Count - best.ReadyUnits > 1)
        {
            return;
        }

        foreach (RequirementEvaluation requirement in best.Requirements.Where(item => item.Ready && item.Unit is not null))
        {
            reservations.Reserve(
                planet.Phase,
                player.AllyCode,
                requirement.Unit!.DefinitionId,
                115,
                $"{mission.Name} en {planet.Name}: pieza de un equipo a una sola mejora de quedar listo.");
        }
    }

    private static void AddUpgradeCandidates(
        ICollection<RiseOfEmpireGuildMissionUpgradeCandidate> upgrades,
        PlayerProfile player,
        RiseOfEmpirePlanetDefinition planet,
        RiseOfEmpireMissionGuideDefinition mission,
        MissionEvaluation evaluation,
        GameDataCatalog gameData)
    {
        if (mission.IsFleet || evaluation.Ready || evaluation.BestTeam is null)
        {
            return;
        }

        RequirementEvaluation[] combined =
        [
            .. evaluation.EntryRequirements,
            .. evaluation.BestTeam.Requirements
        ];
        RequirementEvaluation[] blockers =
        [
            .. combined
                .Where(item => !item.Ready)
                .GroupBy(item => item.Unit?.DefinitionId ?? $"missing:{Normalize(item.Requirement.Label)}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Requirement.MinimumRelicTier).First())
        ];
        if (blockers.Length is 0 or > 2)
        {
            return;
        }

        RequirementEvaluation[] relicCandidates =
        [
            .. blockers.Where(item =>
                item.Unit is not null
                && !item.Requirement.IsShip
                && item.Unit.Rarity >= item.Requirement.MinimumRarity
                && item.Unit.RelicTier < item.Requirement.MinimumRelicTier)
        ];
        if (relicCandidates.Length != blockers.Length)
        {
            return;
        }

        bool completesTeam = blockers.Length == 1;
        foreach (RequirementEvaluation candidate in relicCandidates)
        {
            RosterUnit unit = candidate.Unit!;
            string name = gameData.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition)
                ? definition.Name
                : candidate.Requirement.Label;
            upgrades.Add(new RiseOfEmpireGuildMissionUpgradeCandidate(
                player.AllyCode,
                player.Name,
                unit.DefinitionId,
                name,
                unit.RelicTier,
                candidate.Requirement.MinimumRelicTier,
                planet.Phase,
                planet.Id,
                planet.Name,
                mission.Id,
                mission.Name,
                evaluation.BestTeam.Definition.Name,
                completesTeam));
        }
    }

    private static RosterUnit? FindUnit(
        PlayerProfile player,
        RiseOfEmpireGuideUnitDefinition requirement,
        GameDataCatalog gameData) => player.Roster
        .Where(unit => unit.IsShip == requirement.IsShip && Matches(unit, requirement.Aliases, gameData))
        .OrderByDescending(unit => IsReady(unit, requirement))
        .ThenByDescending(unit => unit.Rarity)
        .ThenByDescending(unit => unit.RelicTier)
        .ThenByDescending(unit => unit.GalacticPower)
        .FirstOrDefault();

    private static bool Matches(
        RosterUnit unit,
        IReadOnlyCollection<string> aliases,
        GameDataCatalog gameData)
    {
        string definitionId = Normalize(unit.DefinitionId);
        string? name = gameData.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition)
            ? Normalize(definition.Name)
            : null;
        return aliases.Any(alias =>
        {
            string normalized = Normalize(alias);
            return string.Equals(definitionId, normalized, StringComparison.Ordinal)
                || (name is not null && string.Equals(name, normalized, StringComparison.Ordinal));
        });
    }

    private static bool IsReady(RosterUnit unit, RiseOfEmpireGuideUnitDefinition requirement) =>
        unit.Rarity >= requirement.MinimumRarity
        && (requirement.IsShip || unit.RelicTier >= requirement.MinimumRelicTier);

    private static int ConfidenceOrder(string confidence) => confidence switch
    {
        "Alta" => 0,
        "Media" => 1,
        _ => 2
    };

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private sealed record RequirementEvaluation(
        RiseOfEmpireGuideUnitDefinition Requirement,
        RosterUnit? Unit,
        bool Ready,
        string MissingMessage);

    private sealed record TeamEvaluation(
        RiseOfEmpireConcreteTeamDefinition Definition,
        bool Ready,
        int ReadyUnits,
        IReadOnlyCollection<RequirementEvaluation> Requirements);

    private sealed record MissionEvaluation(
        bool EntryEligible,
        bool Ready,
        IReadOnlyCollection<RequirementEvaluation> EntryRequirements,
        IReadOnlyCollection<TeamEvaluation> Teams,
        TeamEvaluation? BestTeam,
        IReadOnlyCollection<string> MissingRequirements);
}

internal sealed record RiseOfEmpireGuildMissionPlanning(
    IReadOnlyCollection<RiseOfEmpireGuildMissionCoverage> Coverage,
    RiseOfEmpireCombatReservationIndex Reservations,
    IReadOnlyCollection<RiseOfEmpireGuildMissionUpgradeCandidate> UpgradeCandidates);

internal sealed record RiseOfEmpireGuildMissionUpgradeCandidate(
    long PlayerAllyCode,
    string PlayerName,
    string DefinitionId,
    string UnitName,
    int CurrentRelicTier,
    int TargetRelicTier,
    int Phase,
    string PlanetId,
    string PlanetName,
    string MissionId,
    string MissionName,
    string TeamName,
    bool CompletesTeam);

internal sealed class RiseOfEmpireCombatReservationIndex
{
    private readonly Dictionary<string, Reservation> reservations = new(StringComparer.OrdinalIgnoreCase);

    public int GetCriticality(int phase, long allyCode, string definitionId) =>
        reservations.TryGetValue(Key(phase, allyCode, definitionId), out Reservation? reservation)
            ? reservation.Criticality
            : 0;

    public string? GetReason(int phase, long allyCode, string definitionId) =>
        reservations.TryGetValue(Key(phase, allyCode, definitionId), out Reservation? reservation)
            ? reservation.Reason
            : null;

    public void Reserve(int phase, long allyCode, string definitionId, int criticality, string reason)
    {
        string key = Key(phase, allyCode, definitionId);
        if (!reservations.TryGetValue(key, out Reservation? current))
        {
            reservations[key] = new Reservation(criticality, reason);
            return;
        }

        int combined = Math.Min(260, Math.Max(current.Criticality, criticality) + 15);
        string combinedReason = string.Equals(current.Reason, reason, StringComparison.OrdinalIgnoreCase)
            ? current.Reason
            : $"{current.Reason} También: {reason}";
        reservations[key] = new Reservation(combined, combinedReason);
    }

    private static string Key(int phase, long allyCode, string definitionId) => $"{phase}:{allyCode}:{definitionId}";

    private sealed record Reservation(int Criticality, string Reason);
}
