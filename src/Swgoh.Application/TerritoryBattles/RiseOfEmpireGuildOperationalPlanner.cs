using Swgoh.Application.GameData;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireGuildOperationalPlanner
{
    public static IReadOnlyCollection<RiseOfEmpireGuildMemberOperationalPlan> Build(
        IReadOnlyCollection<PlayerProfile> players,
        RiseOfEmpireGuildMissionAttemptPlanning attempts,
        IReadOnlyCollection<RiseOfEmpireOperationPlan> operations,
        GameDataCatalog gameData)
    {
        var result = new List<RiseOfEmpireGuildMemberOperationalPlan>(players.Count);

        foreach (PlayerProfile player in players.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var phases = new List<RiseOfEmpireGuildMemberPhaseOperationalPlan>();
            for (int phase = 1; phase <= 6; phase++)
            {
                RiseOfEmpirePlannedMissionAttempt[] phaseAttempts =
                [
                    .. attempts.PlannedAttempts
                        .Where(item => item.PlayerAllyCode == player.AllyCode && item.Phase == phase)
                ];
                RiseOfEmpireOperationAssignment[] assignments =
                [
                    .. operations
                        .Where(operation => operation.Phase == phase)
                        .SelectMany(operation => operation.Squads.SelectMany(squad => squad.Assignments
                            .Where(assignment => assignment.PlayerAllyCode == player.AllyCode)
                            .Select(assignment => (operation, assignment))))
                        .Select(item => item.assignment)
                ];

                if (phaseAttempts.Length == 0 && assignments.Length == 0)
                {
                    continue;
                }

                HashSet<string> reservedIds = phaseAttempts
                    .SelectMany(item => item.UnitDefinitionIds)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                RiseOfEmpireMemberMissionAttemptPlan[] missionPlans =
                [
                    .. phaseAttempts.Select((attempt, index) => new RiseOfEmpireMemberMissionAttemptPlan(
                        index + 1,
                        attempt.PlanetId,
                        attempt.PlanetName,
                        attempt.MissionId,
                        attempt.MissionName,
                        attempt.TeamName,
                        [.. attempt.UnitDefinitionIds.Select(id => Unit(id, attempt.RequiredUnitDefinitionIds.Contains(id), gameData))]))
                ];

                RiseOfEmpireMemberOperationDonationPlan[] donations =
                [
                    .. operations
                        .Where(operation => operation.Phase == phase)
                        .SelectMany(operation => operation.Squads.SelectMany(squad => squad.Assignments
                            .Where(assignment => assignment.PlayerAllyCode == player.AllyCode)
                            .Select(assignment => new RiseOfEmpireMemberOperationDonationPlan(
                                operation.Id,
                                operation.PlanetName,
                                squad.Id,
                                assignment.BaseId,
                                assignment.UnitName,
                                assignment.RequiredRelicTier,
                                assignment.CurrentRelicTier,
                                reservedIds.Contains(assignment.BaseId),
                                assignment.AssignmentReason))))
                ];

                RiseOfEmpireReservedUnitPlan[] reservedUnits =
                [
                    .. phaseAttempts
                        .SelectMany(attempt => attempt.UnitDefinitionIds.Select(id => (attempt, id)))
                        .GroupBy(item => item.id, StringComparer.OrdinalIgnoreCase)
                        .Select(group => new RiseOfEmpireReservedUnitPlan(
                            group.Key,
                            UnitName(group.Key, gameData),
                            [.. group.Select(item => item.attempt.MissionName).Distinct(StringComparer.OrdinalIgnoreCase)]))
                        .OrderBy(item => item.UnitName, StringComparer.OrdinalIgnoreCase)
                ];

                phases.Add(new RiseOfEmpireGuildMemberPhaseOperationalPlan(
                    phase,
                    missionPlans,
                    [.. donations.Where(item => !item.BreaksPlannedAttempt)],
                    [.. donations.Where(item => item.BreaksPlannedAttempt)],
                    reservedUnits));
            }

            result.Add(new RiseOfEmpireGuildMemberOperationalPlan(
                player.AllyCode,
                player.Name,
                phases));
        }

        return result;
    }

    private static RiseOfEmpireOperationalUnit Unit(
        string definitionId,
        bool required,
        GameDataCatalog gameData) =>
        new(definitionId, UnitName(definitionId, gameData), required);

    private static string UnitName(string definitionId, GameDataCatalog gameData) =>
        gameData.Units.TryGetValue(definitionId, out GameUnitDefinition? definition)
            ? definition.Name
            : definitionId;
}
