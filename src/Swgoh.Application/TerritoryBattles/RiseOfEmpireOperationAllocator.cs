using System.Globalization;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireOperationAllocator
{
    public static IReadOnlyCollection<RiseOfEmpireOperationPlan> Allocate(
        IReadOnlyCollection<PlayerProfile> players,
        IReadOnlyCollection<RiseOfEmpireOperationDefinition> operations,
        GameDataCatalog gameData)
    {
        var usedUnits = new HashSet<string>(StringComparer.Ordinal);
        var memberAssignments = new Dictionary<string, int>(StringComparer.Ordinal);
        var plans = new List<RiseOfEmpireOperationPlan>();

        foreach (RiseOfEmpireOperationDefinition operation in operations
                     .OrderBy(item => item.Phase)
                     .ThenBy(item => item.PlanetName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var squadPlans = new List<RiseOfEmpireOperationSquadPlan>();
            foreach (RiseOfEmpireOperationSquadDefinition squad in OrderSquads(operation, players))
            {
                squadPlans.Add(AllocateSquad(
                    operation,
                    squad,
                    players,
                    gameData,
                    usedUnits,
                    memberAssignments));
            }

            int totalSlots = operation.Squads.Sum(squad => squad.Units.Count);
            int filledSlots = squadPlans.Where(squad => squad.Complete).Sum(squad => squad.Assignments.Count);
            long completedPoints = squadPlans.Where(squad => squad.Complete).Sum(squad => squad.Points);
            long forcedGp = squadPlans
                .Where(squad => squad.Complete)
                .SelectMany(squad => squad.Assignments)
                .Sum(assignment => assignment.UnitGalacticPower);
            plans.Add(new RiseOfEmpireOperationPlan(
                operation.Id,
                operation.Phase,
                operation.PlanetName,
                operation.Type,
                operation.IsBonus,
                operation.TotalPoints,
                totalSlots,
                filledSlots,
                completedPoints,
                forcedGp,
                squadPlans));
        }

        return plans;
    }

    private static IEnumerable<RiseOfEmpireOperationSquadDefinition> OrderSquads(
        RiseOfEmpireOperationDefinition operation,
        IReadOnlyCollection<PlayerProfile> players) => operation.Squads
        .OrderBy(squad => squad.Units.Count(unit => EligibleCandidates(players, unit).Count == 0))
        .ThenBy(squad => squad.Units.Sum(unit => EligibleCandidates(players, unit).Count))
        .ThenByDescending(squad => squad.Points)
        .ThenBy(squad => squad.Id, StringComparer.Ordinal);

    private static RiseOfEmpireOperationSquadPlan AllocateSquad(
        RiseOfEmpireOperationDefinition operation,
        RiseOfEmpireOperationSquadDefinition squad,
        IReadOnlyCollection<PlayerProfile> players,
        GameDataCatalog gameData,
        HashSet<string> usedUnits,
        Dictionary<string, int> memberAssignments)
    {
        var tentativeUsed = new HashSet<string>(usedUnits, StringComparer.Ordinal);
        var tentativeCounts = new Dictionary<string, int>(memberAssignments, StringComparer.Ordinal);
        var assignments = new List<RiseOfEmpireOperationAssignment>();
        var missing = new List<RiseOfEmpireOperationMissingSlot>();

        var orderedRequirements = squad.Units
            .Select((unit, index) => new Slot(unit, index, EligibleCandidates(players, unit).Count))
            .OrderBy(slot => slot.EligibleCount)
            .ThenByDescending(slot => slot.Unit.RequiredRelicTier)
            .ThenBy(slot => slot.Unit.BaseId, StringComparer.Ordinal)
            .ThenBy(slot => slot.Index)
            .ToArray();

        foreach (Slot slot in orderedRequirements)
        {
            OperationCandidate? candidate = EligibleCandidates(players, slot.Unit)
                .Where(item => !tentativeUsed.Contains(UnitKey(operation.Phase, item.Player.AllyCode, slot.Unit.BaseId)))
                .OrderBy(item => CombatCriticality(item, operation.Phase, gameData))
                .ThenBy(item => GetAssignmentCount(tentativeCounts, operation.Phase, item.Player.AllyCode))
                .ThenBy(item => Math.Max(0, item.Unit.RelicTier - slot.Unit.RequiredRelicTier))
                .ThenBy(item => item.Unit.GalacticPower)
                .ThenBy(item => item.Player.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (candidate is null)
            {
                missing.Add(ToMissingSlot(slot.Unit, players));
                continue;
            }

            string unitKey = UnitKey(operation.Phase, candidate.Player.AllyCode, slot.Unit.BaseId);
            tentativeUsed.Add(unitKey);
            string memberKey = MemberPhaseKey(operation.Phase, candidate.Player.AllyCode);
            tentativeCounts[memberKey] = GetAssignmentCount(tentativeCounts, operation.Phase, candidate.Player.AllyCode) + 1;
            int criticality = CombatCriticality(candidate, operation.Phase, gameData);
            assignments.Add(new RiseOfEmpireOperationAssignment(
                slot.Unit.BaseId,
                slot.Unit.Name,
                slot.Unit.IsShip,
                slot.Unit.RequiredRelicTier,
                candidate.Player.AllyCode,
                candidate.Player.Name,
                candidate.Unit.RelicTier,
                candidate.Unit.GalacticPower,
                criticality,
                criticality >= 80
                    ? "Asignación necesaria, pero la unidad es crítica para combate o desbloqueo en esta fase."
                    : criticality > 0
                        ? "Asignación válida; se ha preferido una copia con menor coste de oportunidad de combate."
                        : "Asignación de bajo coste de oportunidad para combate."));
        }

        bool complete = missing.Count == 0 && assignments.Count == squad.Units.Count;
        if (complete)
        {
            foreach (string key in tentativeUsed.Except(usedUnits, StringComparer.Ordinal))
            {
                usedUnits.Add(key);
            }

            foreach ((string key, int value) in tentativeCounts)
            {
                memberAssignments[key] = value;
            }
        }
        else
        {
            assignments.Clear();
        }

        return new RiseOfEmpireOperationSquadPlan(
            squad.Id,
            squad.Points,
            complete,
            assignments,
            missing);
    }

    private static List<OperationCandidate> EligibleCandidates(
        IReadOnlyCollection<PlayerProfile> players,
        RiseOfEmpireOperationUnitDefinition requirement) =>
        [
            .. players.SelectMany(player => player.Roster
                    .Where(unit => string.Equals(unit.DefinitionId, requirement.BaseId, StringComparison.OrdinalIgnoreCase))
                    .Select(unit => new OperationCandidate(player, unit)))
                .Where(candidate => IsEligible(candidate.Unit, requirement))
        ];

    private static bool IsEligible(RosterUnit unit, RiseOfEmpireOperationUnitDefinition requirement) =>
        unit.IsShip == requirement.IsShip
        && unit.Rarity >= requirement.RequiredRarity
        && (requirement.IsShip || unit.RelicTier >= requirement.RequiredRelicTier);

    private static RiseOfEmpireOperationMissingSlot ToMissingSlot(
        RiseOfEmpireOperationUnitDefinition requirement,
        IReadOnlyCollection<PlayerProfile> players)
    {
        RiseOfEmpireNearCandidate[] near =
        [
            .. players.SelectMany(player => player.Roster
                    .Where(unit => string.Equals(unit.DefinitionId, requirement.BaseId, StringComparison.OrdinalIgnoreCase))
                    .Select(unit => new RiseOfEmpireNearCandidate(
                        player.AllyCode,
                        player.Name,
                        unit.Rarity,
                        unit.RelicTier,
                        requirement.IsShip ? 0 : Math.Max(0, requirement.RequiredRelicTier - unit.RelicTier))))
                .OrderBy(candidate => candidate.CurrentRarity < requirement.RequiredRarity ? 1 : 0)
                .ThenBy(candidate => candidate.RelicsMissing)
                .ThenByDescending(candidate => candidate.CurrentRelicTier)
                .Take(5)
        ];
        return new RiseOfEmpireOperationMissingSlot(
            requirement.BaseId,
            requirement.Name,
            requirement.IsShip,
            requirement.RequiredRarity,
            requirement.RequiredRelicTier,
            near);
    }

    private static int CombatCriticality(OperationCandidate candidate, int phase, GameDataCatalog gameData)
    {
        if (!gameData.Units.TryGetValue(candidate.Unit.DefinitionId, out GameUnitDefinition? definition))
        {
            return 0;
        }

        int score = 0;
        foreach (RiseOfEmpirePlanetDefinition planet in RiseOfEmpireCatalog.Planets.Where(planet => planet.Phase == phase))
        {
            IEnumerable<RiseOfEmpireMissionDefinition> missions = planet.Missions
                .Concat(planet.AccessRequirement is null ? [] : [planet.AccessRequirement]);
            foreach (RiseOfEmpireMissionDefinition mission in missions)
            {
                if (mission.UnitRequirements.Any(requirement => requirement.Aliases.Any(alias =>
                        string.Equals(Normalize(alias), Normalize(candidate.Unit.DefinitionId), StringComparison.Ordinal))))
                {
                    score = Math.Max(score, mission.Type.Contains("Unlock", StringComparison.OrdinalIgnoreCase) ? 120 : 90);
                }

                if (!string.IsNullOrWhiteSpace(mission.FactionTagKeyword)
                    && MatchesTag(definition, mission.FactionTagKeyword))
                {
                    score = Math.Max(score, mission.Type.Contains("Unlock", StringComparison.OrdinalIgnoreCase) ? 100 : 60);
                }
            }

            if (planet.Archetypes.Any(archetype => MatchesTag(definition, archetype.TagKeyword)))
            {
                score = Math.Max(score, 25);
            }
        }

        return score;
    }

    private static bool MatchesTag(GameUnitDefinition definition, string keyword)
    {
        string normalized = Normalize(keyword);
        return definition.Tags.Any(tag => Normalize(tag).Contains(normalized, StringComparison.Ordinal))
            || definition.Factions.Any(faction => Normalize(faction).Contains(normalized, StringComparison.Ordinal));
    }

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

    private static int GetAssignmentCount(Dictionary<string, int> counts, int phase, long allyCode) =>
        counts.GetValueOrDefault(MemberPhaseKey(phase, allyCode));

    private static string UnitKey(int phase, long allyCode, string baseId) => $"{phase}:{allyCode}:{baseId}";

    private static string MemberPhaseKey(int phase, long allyCode) => $"{phase}:{allyCode}";

    private sealed record Slot(RiseOfEmpireOperationUnitDefinition Unit, int Index, int EligibleCount);

    private sealed record OperationCandidate(PlayerProfile Player, RosterUnit Unit);
}
