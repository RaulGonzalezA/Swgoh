using System.Globalization;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireGuildMissionAttemptPlanner
{
    public static RiseOfEmpireGuildMissionAttemptPlanning Plan(
        IReadOnlyCollection<PlayerProfile> players,
        GameDataCatalog gameData,
        RiseOfEmpireCombatReservationIndex reservations)
    {
        var planned = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
        var blocked = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);

        foreach (PlayerProfile player in players)
        {
            for (int phase = 1; phase <= 6; phase++)
            {
                MissionCandidate[] candidates = BuildCandidates(player, phase, gameData);
                if (candidates.Length == 0)
                {
                    continue;
                }

                IReadOnlyCollection<SelectedAttempt> selected = SelectMaximumAttempts(candidates);
                HashSet<string> selectedMissionKeys = selected
                    .Select(item => item.MissionKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (SelectedAttempt attempt in selected)
                {
                    AddMember(planned, attempt.MissionKey, player.AllyCode);
                    ReserveAttempt(reservations, player, attempt);
                }

                foreach (MissionCandidate candidate in candidates.Where(candidate => candidate.Options.Count > 0))
                {
                    if (!selectedMissionKeys.Contains(candidate.MissionKey))
                    {
                        AddMember(blocked, candidate.MissionKey, player.AllyCode);
                    }
                }
            }
        }

        return new RiseOfEmpireGuildMissionAttemptPlanning(planned, blocked);
    }

    public static string MissionKey(int phase, string planetId, string missionId) =>
        $"{phase}:{planetId}:{missionId}";

    private static MissionCandidate[] BuildCandidates(PlayerProfile player, int phase, GameDataCatalog gameData)
    {
        var candidates = new List<MissionCandidate>();
        foreach (RiseOfEmpirePlanetDefinition planet in RiseOfEmpireCatalog.Planets.Where(item => item.Phase == phase))
        {
            if (!RiseOfEmpireMissionGuideCatalog.ByPlanet.TryGetValue(
                    planet.Id,
                    out IReadOnlyCollection<RiseOfEmpireMissionGuideDefinition>? missions))
            {
                continue;
            }

            foreach (RiseOfEmpireMissionGuideDefinition mission in missions)
            {
                RosterUnit[] entryUnits = ResolveRequirements(player, mission.RequiredUnits, gameData);
                bool entryReady = entryUnits.Length == mission.RequiredUnits.Count;
                var options = new List<MissionTeamOption>();
                if (entryReady)
                {
                    foreach (RiseOfEmpireConcreteTeamDefinition team in mission.RecommendedTeams)
                    {
                        RosterUnit[] teamUnits = ResolveRequirements(player, team.Units, gameData);
                        if (teamUnits.Length != team.Units.Count)
                        {
                            continue;
                        }

                        string[] unitIds = entryUnits
                            .Concat(teamUnits)
                            .Select(unit => unit.DefinitionId)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        options.Add(new MissionTeamOption(
                            team.Name,
                            ConfidenceScore(team.Confidence),
                            unitIds,
                            entryUnits.Select(unit => unit.DefinitionId)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase)));
                    }
                }

                candidates.Add(new MissionCandidate(
                    MissionKey(phase, planet.Id, mission.Id),
                    phase,
                    planet.Name,
                    mission.Name,
                    options));
            }
        }

        return
        [
            .. candidates
                .OrderBy(candidate => candidate.Options.Count == 0 ? int.MaxValue : candidate.Options.Count)
                .ThenBy(candidate => candidate.PlanetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.MissionName, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static RosterUnit[] ResolveRequirements(
        PlayerProfile player,
        IReadOnlyCollection<RiseOfEmpireGuideUnitDefinition> requirements,
        GameDataCatalog gameData)
    {
        var selected = new List<RosterUnit>(requirements.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RiseOfEmpireGuideUnitDefinition requirement in requirements)
        {
            RosterUnit? unit = player.Roster
                .Where(unit => !used.Contains(unit.DefinitionId))
                .Where(unit => unit.IsShip == requirement.IsShip && Matches(unit, requirement.Aliases, gameData))
                .Where(unit => IsReady(unit, requirement))
                .OrderByDescending(unit => unit.Rarity)
                .ThenByDescending(unit => unit.RelicTier)
                .ThenByDescending(unit => unit.GalacticPower)
                .FirstOrDefault();
            if (unit is null)
            {
                return [];
            }

            selected.Add(unit);
            used.Add(unit.DefinitionId);
        }

        return [.. selected];
    }

    private static IReadOnlyCollection<SelectedAttempt> SelectMaximumAttempts(
        IReadOnlyCollection<MissionCandidate> candidates)
    {
        MissionCandidate[] missions = [.. candidates.Where(candidate => candidate.Options.Count > 0)];
        if (missions.Length == 0)
        {
            return [];
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new List<SelectedAttempt>();
        SelectedAttempt[] best = [];
        int bestConfidence = -1;

        Search(0, 0);
        return best;

        void Search(int index, int confidence)
        {
            if (current.Count + (missions.Length - index) < best.Length)
            {
                return;
            }

            if (index == missions.Length)
            {
                if (current.Count > best.Length ||
                    (current.Count == best.Length && confidence > bestConfidence))
                {
                    best = [.. current];
                    bestConfidence = confidence;
                }

                return;
            }

            MissionCandidate mission = missions[index];
            foreach (MissionTeamOption option in mission.Options
                         .OrderByDescending(option => option.Confidence)
                         .ThenBy(option => option.UnitIds.Count))
            {
                if (option.UnitIds.Any(used.Contains))
                {
                    continue;
                }

                foreach (string unitId in option.UnitIds)
                {
                    used.Add(unitId);
                }

                current.Add(new SelectedAttempt(
                    mission.MissionKey,
                    mission.Phase,
                    mission.PlanetName,
                    mission.MissionName,
                    option));
                Search(index + 1, confidence + option.Confidence);
                current.RemoveAt(current.Count - 1);

                foreach (string unitId in option.UnitIds)
                {
                    used.Remove(unitId);
                }
            }

            Search(index + 1, confidence);
        }
    }

    private static void ReserveAttempt(
        RiseOfEmpireCombatReservationIndex reservations,
        PlayerProfile player,
        SelectedAttempt attempt)
    {
        foreach (string definitionId in attempt.Option.UnitIds)
        {
            bool mandatory = attempt.Option.RequiredUnitIds.Contains(definitionId);
            reservations.Reserve(
                attempt.Phase,
                player.AllyCode,
                definitionId,
                mandatory ? 260 : 235,
                mandatory
                    ? $"{attempt.MissionName} en {attempt.PlanetName}: unidad obligatoria reservada para un intento planificado."
                    : $"{attempt.MissionName} en {attempt.PlanetName}: unidad reservada para maximizar los intentos de misión del miembro.");
        }
    }

    private static void AddMember(Dictionary<string, HashSet<long>> map, string missionKey, long allyCode)
    {
        if (!map.TryGetValue(missionKey, out HashSet<long>? members))
        {
            members = new HashSet<long>();
            map[missionKey] = members;
        }

        members.Add(allyCode);
    }

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

    private static int ConfidenceScore(string confidence) => confidence switch
    {
        "Alta" => 3,
        "Media" => 2,
        _ => 1
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

    private sealed record MissionCandidate(
        string MissionKey,
        int Phase,
        string PlanetName,
        string MissionName,
        IReadOnlyCollection<MissionTeamOption> Options);

    private sealed record MissionTeamOption(
        string TeamName,
        int Confidence,
        IReadOnlyCollection<string> UnitIds,
        IReadOnlySet<string> RequiredUnitIds);

    private sealed record SelectedAttempt(
        string MissionKey,
        int Phase,
        string PlanetName,
        string MissionName,
        MissionTeamOption Option);
}

internal sealed record RiseOfEmpireGuildMissionAttemptPlanning(
    IReadOnlyDictionary<string, HashSet<long>> PlannedMembers,
    IReadOnlyDictionary<string, HashSet<long>> BlockedMembers)
{
    public IReadOnlySet<long> PlannedFor(string missionKey) =>
        PlannedMembers.TryGetValue(missionKey, out HashSet<long>? members)
            ? members
            : Empty;

    public IReadOnlySet<long> BlockedFor(string missionKey) =>
        BlockedMembers.TryGetValue(missionKey, out HashSet<long>? members)
            ? members
            : Empty;

    private static readonly HashSet<long> Empty = [];
}
