using Swgoh.Application.GameData;
using Swgoh.Application.TerritoryBattles;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.TerritoryBattles;

public sealed class RiseOfEmpireGuildOperationalPlannerTests
{
    [Fact]
    public void Build_SeparatesSafeDonationsFromMissionConflicts()
    {
        PlayerProfile player = Player(100_000_001, "Uno", Unit("A"), Unit("B"), Unit("C"));
        var attempts = new RiseOfEmpireGuildMissionAttemptPlanning(
            new Dictionary<string, HashSet<long>>(),
            new Dictionary<string, HashSet<long>>(),
            [
                new RiseOfEmpirePlannedMissionAttempt(
                    player.AllyCode,
                    player.Name,
                    1,
                    "mustafar",
                    "Mustafar",
                    "mission-a",
                    "Misión A",
                    "Equipo A",
                    ["A", "B"],
                    ["A"])
            ]);
        var operation = new RiseOfEmpireOperationPlan(
            "op-1",
            1,
            "Mustafar",
            "DS",
            false,
            10_000_000,
            2,
            2,
            10_000_000,
            60_000,
            [
                new RiseOfEmpireOperationSquadPlan(
                    "squad",
                    10_000_000,
                    true,
                    [
                        Assignment("A", player, 260),
                        Assignment("C", player, 0)
                    ],
                    [])
            ]);

        RiseOfEmpireGuildMemberOperationalPlan plan = Assert.Single(
            RiseOfEmpireGuildOperationalPlanner.Build([player], attempts, [operation], EmptyCatalog()));
        RiseOfEmpireGuildMemberPhaseOperationalPlan phase = Assert.Single(plan.Phases);

        Assert.Single(phase.MissionAttempts);
        Assert.Contains(phase.ReservedUnits, unit => unit.DefinitionId == "A");
        Assert.Contains(phase.ReservedUnits, unit => unit.DefinitionId == "B");
        Assert.Single(phase.SafeOperationDonations, donation => donation.DefinitionId == "C");
        Assert.Single(phase.OperationConflicts, donation => donation.DefinitionId == "A" && donation.BreaksPlannedAttempt);
    }

    private static RiseOfEmpireOperationAssignment Assignment(
        string definitionId,
        PlayerProfile player,
        int criticality) => new(
        definitionId,
        definitionId,
        false,
        5,
        player.AllyCode,
        player.Name,
        7,
        30_000,
        criticality,
        "test");

    private static PlayerProfile Player(long allyCode, string name, params RosterUnit[] roster) => PlayerProfile.Import(
        allyCode,
        $"player-{allyCode}",
        name,
        "guild",
        "Guild",
        85,
        roster.Sum(unit => unit.GalacticPower),
        DateTimeOffset.UtcNow,
        roster);

    private static RosterUnit Unit(string definitionId) => new(
        definitionId,
        definitionId,
        85,
        7,
        13,
        7,
        6,
        30_000);

    private static GameDataCatalog EmptyCatalog() => new(
        new Dictionary<string, GameUnitDefinition>(),
        new Dictionary<string, GameSkillDefinition>(),
        []);
}
