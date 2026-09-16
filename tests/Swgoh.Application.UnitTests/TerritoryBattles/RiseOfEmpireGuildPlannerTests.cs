using Swgoh.Application.GameData;
using Swgoh.Application.TerritoryBattles;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.TerritoryBattles;

public sealed class RiseOfEmpireGuildPlannerTests
{
    [Fact]
    public void Allocate_DoesNotUseSamePlayerUnitTwiceInOnePhase()
    {
        PlayerProfile player = Player(100_000_001, "Uno", Unit("HERO", 7));
        var operation = new RiseOfEmpireOperationDefinition(
            "P1-C1",
            1,
            "Coruscant",
            "LS",
            false,
            20_000_000,
            [
                Squad("one", 10_000_000, Requirement("HERO", 5)),
                Squad("two", 10_000_000, Requirement("HERO", 5))
            ]);

        RiseOfEmpireOperationPlan plan = Assert.Single(RiseOfEmpireOperationAllocator.Allocate(
            [player],
            [operation],
            EmptyCatalog()));

        Assert.Equal(1, plan.FilledSlots);
        Assert.Single(plan.Squads, squad => squad.Complete);
        Assert.Single(plan.Squads, squad => !squad.Complete);
        Assert.Single(plan.Squads.SelectMany(squad => squad.Assignments));
    }

    [Fact]
    public void BonusUnlockAnalyzer_UsesGuildClearThresholds()
    {
        var players = new List<PlayerProfile>();
        for (int index = 0; index < 30; index++)
        {
            var roster = new List<RosterUnit>
            {
                Unit("CEREJUNDA", 7),
                Unit("CALKESTIS", 7)
            };
            if (index < 25)
            {
                roster.Add(Unit("BOKATANMANDALORE", 7));
                roster.Add(Unit("THEMANDALORIANBESKARARMOR", 7));
                roster.Add(Unit("MANDO3", 7));
            }

            players.Add(Player(100_000_001 + index, $"P{index + 1}", [.. roster]));
        }

        GameDataCatalog catalog = Catalog(Definition("MANDO3", "Mando 3", "affiliation_mandalorian"));
        IReadOnlyCollection<RiseOfEmpireBonusUnlockReadiness> result =
            RiseOfEmpireBonusUnlockAnalyzer.Analyze(players, catalog);

        RiseOfEmpireBonusUnlockReadiness zeffo = Assert.Single(result, item => item.PlanetName == "Zeffo");
        RiseOfEmpireBonusUnlockReadiness mandalore = Assert.Single(result, item => item.PlanetName == "Mandalore");
        Assert.Equal(30, zeffo.EligibleMembers);
        Assert.Equal(30, zeffo.RequiredClears);
        Assert.True(zeffo.ProjectedUnlocked);
        Assert.Equal(25, mandalore.EligibleMembers);
        Assert.Equal(25, mandalore.RequiredClears);
        Assert.True(mandalore.ProjectedUnlocked);
    }

    [Fact]
    public void RoutePlanner_MaximizesStarsWithinAvailableGuildGp()
    {
        IReadOnlyCollection<RiseOfEmpireGuildPhasePlan> phases = RiseOfEmpireGuildRoutePlanner.Build(
            240_000_000,
            [],
            [LockedBonus("Zeffo", "Bracca", 30), LockedBonus("Mandalore", "Tatooine", 25)]);

        RiseOfEmpireGuildPhasePlan phaseOne = Assert.Single(phases, phase => phase.Phase == 1);
        Assert.Equal(3, phaseOne.ProjectedStars);
        Assert.True(phaseOne.Planets.Sum(planet => planet.AdditionalDeploymentGalacticPower) <= 240_000_000);
        Assert.Contains(phaseOne.Planets, planet => planet.TargetStars == 3);
    }

    [Fact]
    public void UpgradePlanner_RanksRelicGapThatBlocksOperation()
    {
        PlayerProfile player = Player(100_000_001, "Uno", Unit("HERO", 4));
        var missing = new RiseOfEmpireOperationMissingSlot(
            "HERO",
            "Hero",
            false,
            7,
            5,
            [new RiseOfEmpireNearCandidate(player.AllyCode, player.Name, 7, 4, 1)]);
        var operation = new RiseOfEmpireOperationPlan(
            "P1-C1",
            1,
            "Coruscant",
            "LS",
            false,
            10_000_000,
            1,
            0,
            0,
            0,
            [new RiseOfEmpireOperationSquadPlan("one", 10_000_000, false, [], [missing])]);

        IReadOnlyCollection<RiseOfEmpireGuildUpgradePriority> result = RiseOfEmpireGuildUpgradePlanner.Build(
            [player],
            [operation],
            [],
            EmptyCatalog());

        RiseOfEmpireGuildUpgradePriority priority = Assert.Single(result, item => item.DefinitionId == "HERO");
        Assert.Equal(4, priority.CurrentRelicTier);
        Assert.Equal(5, priority.TargetRelicTier);
        Assert.Contains(priority.Reasons, reason => reason.Contains("operación", StringComparison.OrdinalIgnoreCase));
    }

    private static RiseOfEmpireOperationSquadDefinition Squad(
        string id,
        long points,
        params RiseOfEmpireOperationUnitDefinition[] units) => new(id, points, units);

    private static RiseOfEmpireOperationUnitDefinition Requirement(string baseId, int relic) =>
        new(baseId, baseId, false, 7, relic);

    private static RiseOfEmpireBonusUnlockReadiness LockedBonus(string planet, string source, int required) =>
        new(planet, source, required, 0, false, [], []);

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

    private static RosterUnit Unit(string definitionId, int relic) => new(
        definitionId,
        definitionId,
        85,
        7,
        13,
        relic,
        6,
        25_000 + relic * 1_000);

    private static GameUnitDefinition Definition(string id, string name, params string[] tags) => new(
        id,
        false,
        null,
        name,
        null,
        [],
        tags);

    private static GameDataCatalog Catalog(params GameUnitDefinition[] units) => new(
        units.ToDictionary(unit => unit.BaseId, StringComparer.Ordinal),
        new Dictionary<string, GameSkillDefinition>(),
        []);

    private static GameDataCatalog EmptyCatalog() => Catalog();
}
