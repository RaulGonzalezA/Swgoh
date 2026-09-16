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
    public void Allocate_PrefersMemberWhoseUnitIsNotReservedForConcreteMissionTeam()
    {
        PlayerProfile combatPlayer = Player(
            100_000_001,
            "Combate",
            Unit("GEONOSIANBROODALPHA", 6),
            Unit("GEONOSIANSPY", 6),
            Unit("GEONOSIANSOLDIER", 6),
            Unit("SUNFAC", 6),
            Unit("POGGLETHELESSER", 6));
        PlayerProfile donor = Player(100_000_002, "Donante", Unit("GEONOSIANBROODALPHA", 6));
        GameDataCatalog catalog = EmptyCatalog();
        RiseOfEmpireGuildMissionPlanning planning = RiseOfEmpireGuildMissionAnalyzer.Analyze([combatPlayer, donor], catalog);
        var operation = new RiseOfEmpireOperationDefinition(
            "P2-C1",
            2,
            "Geonosis",
            "DS",
            false,
            10_000_000,
            [Squad("one", 10_000_000, Requirement("GEONOSIANBROODALPHA", 6))]);

        RiseOfEmpireOperationPlan plan = Assert.Single(RiseOfEmpireOperationAllocator.Allocate(
            [combatPlayer, donor],
            [operation],
            catalog,
            planning.Reservations));

        RiseOfEmpireOperationAssignment assignment = Assert.Single(plan.Squads.SelectMany(squad => squad.Assignments));
        Assert.Equal(donor.AllyCode, assignment.PlayerAllyCode);
        Assert.Equal(0, assignment.CombatCriticality);
    }

    [Fact]
    public void MissionAnalyzer_ListsMembersReadyForConcreteMission()
    {
        PlayerProfile ready = Player(
            100_000_001,
            "Listo",
            Unit("GEONOSIANBROODALPHA", 6),
            Unit("GEONOSIANSPY", 6),
            Unit("GEONOSIANSOLDIER", 6),
            Unit("SUNFAC", 6),
            Unit("POGGLETHELESSER", 6));
        PlayerProfile close = Player(
            100_000_002,
            "Cerca",
            Unit("GEONOSIANBROODALPHA", 6),
            Unit("GEONOSIANSPY", 6),
            Unit("GEONOSIANSOLDIER", 6),
            Unit("SUNFAC", 6),
            Unit("POGGLETHELESSER", 5));

        RiseOfEmpireGuildMissionPlanning planning = RiseOfEmpireGuildMissionAnalyzer.Analyze([ready, close], EmptyCatalog());

        RiseOfEmpireGuildMissionCoverage mission = Assert.Single(
            planning.Coverage,
            item => item.MissionId == "geonosis-geos");
        RiseOfEmpireGuildMissionMember readyMember = Assert.Single(mission.ReadyMembers);
        Assert.Equal(ready.AllyCode, readyMember.AllyCode);
        Assert.Equal(2, mission.TargetAttempts);
        Assert.Contains(mission.PlannedMembers, member => member.AllyCode == ready.AllyCode);
        Assert.Contains(mission.ClosestMembers, member => member.AllyCode == close.AllyCode && member.ReadyUnits == 4);
    }

    [Fact]
    public void MissionAnalyzer_UsesAlternativeTeamsToMaximizeAttemptsPerMember()
    {
        PlayerProfile player = Player(
            100_000_001,
            "Completo",
            Unit("LORDVADER", 5),
            Unit("DARTHVADER", 5),
            Unit("ROYALGUARD", 5),
            Unit("MAULS7", 5),
            Unit("GRANDMOFFTARKIN", 5),
            Unit("SUPREMELEADERKYLOREN", 5),
            Unit("KYLORENUNMASKED", 5),
            Unit("GENERALHUX", 5),
            Unit("FOSITHTROOPER", 5),
            Unit("FIRSTORDERSTORMTROOPER", 5));

        RiseOfEmpireGuildMissionPlanning planning = RiseOfEmpireGuildMissionAnalyzer.Analyze([player], EmptyCatalog());
        RiseOfEmpireGuildMissionCoverage generic = Assert.Single(planning.Coverage, item => item.MissionId == "mustafar-dark");
        RiseOfEmpireGuildMissionCoverage lordVader = Assert.Single(planning.Coverage, item => item.MissionId == "mustafar-lv");

        Assert.Single(generic.PlannedMembers);
        Assert.Single(lordVader.PlannedMembers);
        Assert.Empty(generic.OverlapBlockedMembers);
        Assert.Empty(lordVader.OverlapBlockedMembers);
    }

    [Fact]
    public void MissionAnalyzer_DoesNotCountSameUnitsAsTwoMissionAttempts()
    {
        PlayerProfile player = Player(
            100_000_001,
            "Solo LV",
            Unit("LORDVADER", 5),
            Unit("DARTHVADER", 5),
            Unit("ROYALGUARD", 5),
            Unit("MAULS7", 5),
            Unit("GRANDMOFFTARKIN", 5));

        RiseOfEmpireGuildMissionPlanning planning = RiseOfEmpireGuildMissionAnalyzer.Analyze([player], EmptyCatalog());
        RiseOfEmpireGuildMissionCoverage[] missions =
        [
            .. planning.Coverage.Where(item => item.MissionId is "mustafar-dark" or "mustafar-lv")
        ];

        Assert.Equal(2, missions.Sum(mission => mission.ReadyMembers.Count));
        Assert.Equal(1, missions.Sum(mission => mission.PlannedMembers.Count));
        Assert.Equal(1, missions.Sum(mission => mission.OverlapBlockedMembers.Count));
        Assert.All(missions, mission => Assert.Equal(1, mission.TargetAttempts));
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
        Assert.Contains(phaseOne.Planets, planet => planet.NextStarGap is not null || planet.TargetStars == 3);
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

    [Fact]
    public void UpgradePlanner_PrioritizesRelicThatCompletesMissionNearNextStar()
    {
        PlayerProfile player = Player(
            100_000_001,
            "Uno",
            Unit("GEONOSIANBROODALPHA", 6),
            Unit("GEONOSIANSPY", 6),
            Unit("GEONOSIANSOLDIER", 6),
            Unit("SUNFAC", 6),
            Unit("POGGLETHELESSER", 5));
        RiseOfEmpireGuildMissionPlanning planning = RiseOfEmpireGuildMissionAnalyzer.Analyze([player], EmptyCatalog());
        var planet = new RiseOfEmpireGuildPlanetPlan(
            "geonosis",
            "Geonosis",
            false,
            true,
            1,
            148_125_000,
            0,
            0,
            148_125_000,
            2,
            237_000_000,
            20_000_000,
            "test");
        var phase = new RiseOfEmpireGuildPhasePlan(2, 240_000_000, 0, 1, [planet]);

        IReadOnlyCollection<RiseOfEmpireGuildUpgradePriority> result = RiseOfEmpireGuildUpgradePlanner.Build(
            [player],
            [],
            [],
            EmptyCatalog(),
            planning.UpgradeCandidates,
            [phase]);

        RiseOfEmpireGuildUpgradePriority poggle = Assert.Single(result, item => item.DefinitionId == "POGGLETHELESSER");
        Assert.Equal(6, poggle.TargetRelicTier);
        Assert.Equal(1, poggle.MissionTeamsUnlocked);
        Assert.Equal(20_000_000, poggle.ClosestNextStarGap);
        Assert.Contains("Geonosis", poggle.AffectedPlanets);
        Assert.Contains(poggle.Reasons, reason => reason.Contains("siguiente estrella", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpgradePlanner_UsesHighestRelicTargetAcrossDifferentNeeds()
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
        var mission = new RiseOfEmpireGuildMissionUpgradeCandidate(
            player.AllyCode,
            player.Name,
            "HERO",
            "Hero",
            4,
            7,
            3,
            "dathomir",
            "Dathomir",
            "mission",
            "Mission",
            "Team",
            true);

        RiseOfEmpireGuildUpgradePriority priority = Assert.Single(RiseOfEmpireGuildUpgradePlanner.Build(
            [player],
            [operation],
            [],
            EmptyCatalog(),
            [mission],
            []), item => item.DefinitionId == "HERO");

        Assert.Equal(7, priority.TargetRelicTier);
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
