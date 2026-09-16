using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Application.TerritoryBattles;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.TerritoryBattles;

public sealed class RiseOfEmpireMissionGuideServiceTests
{
    private const long AllyCode = 123456789;
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_MarksConcreteMissionTeamReadyFromRoster()
    {
        PlayerProfile player = CreatePlayer(
            Character("QIRA", 5),
            Character("GLREY", 8),
            Character("VANDORCHEWBACCA", 5),
            Character("L3_37", 5),
            Character("YOUNGHAN", 5));
        GameDataCatalog catalog = Catalog(
            CharacterDefinition("QIRA", "Qi'ra"),
            CharacterDefinition("GLREY", "Rey"),
            CharacterDefinition("VANDORCHEWBACCA", "Vandor Chewbacca"),
            CharacterDefinition("L3_37", "L3-37"),
            CharacterDefinition("YOUNGHAN", "Young Han Solo"));
        var service = new RiseOfEmpireMissionGuideService(new FakePlayerProfileService(player), new FakeCatalog(catalog));

        RiseOfEmpireMissionGuideAnalysis? analysis = await service.GetAsync(AllyCode, TestContext.Current.CancellationToken);

        Assert.NotNull(analysis);
        RiseOfEmpireMissionGuide mission = analysis.Planets
            .Single(planet => planet.PlanetId == "corellia")
            .Missions.Single(item => item.Id == "corellia-qira");
        Assert.True(mission.Eligible);
        RiseOfEmpireConcreteTeamRecommendation team = Assert.Single(mission.RecommendedTeams);
        Assert.True(team.Ready);
        Assert.Equal(5, team.ReadyUnits);
    }

    [Fact]
    public async Task GetAsync_ReportsRelicGapInsideConcreteMissionTeam()
    {
        PlayerProfile player = CreatePlayer(
            Character("QIRA", 5),
            Character("GLREY", 8),
            Character("VANDORCHEWBACCA", 5),
            Character("L3_37", 5),
            Character("YOUNGHAN", 4));
        GameDataCatalog catalog = Catalog(
            CharacterDefinition("QIRA", "Qi'ra"),
            CharacterDefinition("GLREY", "Rey"),
            CharacterDefinition("VANDORCHEWBACCA", "Vandor Chewbacca"),
            CharacterDefinition("L3_37", "L3-37"),
            CharacterDefinition("YOUNGHAN", "Young Han Solo"));
        var service = new RiseOfEmpireMissionGuideService(new FakePlayerProfileService(player), new FakeCatalog(catalog));

        RiseOfEmpireMissionGuideAnalysis? analysis = await service.GetAsync(AllyCode, TestContext.Current.CancellationToken);

        RiseOfEmpireMissionGuide mission = analysis!.Planets
            .Single(planet => planet.PlanetId == "corellia")
            .Missions.Single(item => item.Id == "corellia-qira");
        RiseOfEmpireConcreteTeamRecommendation team = Assert.Single(mission.RecommendedTeams);
        Assert.False(team.Ready);
        Assert.Equal(4, team.ReadyUnits);
        Assert.Contains(team.MissingUnits, item => item.Contains("Young Han Solo: R4 → R5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_RequiresSevenStarShipsForFleetRecommendation()
    {
        PlayerProfile player = CreatePlayer(
            Ship("PROFUNDITY", 7),
            Ship("YWINGREBEL", 7),
            Ship("MILLENNIUMFALCON", 7),
            Ship("OUTRIDER", 6),
            Ship("PHANTOM2", 7));
        GameDataCatalog catalog = Catalog(
            ShipDefinition("PROFUNDITY", "Profundity"),
            ShipDefinition("YWINGREBEL", "Rebel Y-wing"),
            ShipDefinition("MILLENNIUMFALCON", "Han's Millennium Falcon"),
            ShipDefinition("OUTRIDER", "Outrider"),
            ShipDefinition("PHANTOM2", "Phantom II"));
        var service = new RiseOfEmpireMissionGuideService(new FakePlayerProfileService(player), new FakeCatalog(catalog));

        RiseOfEmpireMissionGuideAnalysis? analysis = await service.GetAsync(AllyCode, TestContext.Current.CancellationToken);

        RiseOfEmpireMissionGuide mission = analysis!.Planets
            .Single(planet => planet.PlanetId == "coruscant")
            .Missions.Single(item => item.Id == "coruscant-fleet");
        Assert.False(mission.Eligible);
        Assert.Contains(mission.MissingRequirements, item => item.Contains("Outrider: 6★ → 7★", StringComparison.Ordinal));
        RiseOfEmpireConcreteTeamRecommendation team = Assert.Single(mission.RecommendedTeams);
        Assert.False(team.Ready);
        Assert.Equal(4, team.ReadyUnits);
        Assert.Contains(team.MissingUnits, item => item.Contains("Outrider: 6★ → 7★", StringComparison.Ordinal));
    }

    private static PlayerProfile CreatePlayer(params RosterUnit[] roster) => PlayerProfile.Import(
        AllyCode,
        "player-id",
        "Tester",
        null,
        null,
        85,
        roster.Sum(unit => unit.GalacticPower),
        Now,
        roster);

    private static RosterUnit Character(string id, int relic) => new(
        id,
        id,
        85,
        7,
        13,
        relic,
        6,
        25_000 + relic * 1_000,
        IsShip: false,
        ZetaCount: 1,
        OmicronCount: 0,
        Stats: new RosterUnitStats(Speed: 250 + relic));

    private static RosterUnit Ship(string id, int rarity) => new(
        id,
        id,
        85,
        rarity,
        13,
        0,
        0,
        60_000 + rarity * 1_000,
        IsShip: true,
        ZetaCount: 0,
        OmicronCount: 0);

    private static GameUnitDefinition CharacterDefinition(string id, string name) => new(
        id, IsShip: false, NameKey: null, name, ThumbnailName: null, Factions: [], Tags: []);

    private static GameUnitDefinition ShipDefinition(string id, string name) => new(
        id, IsShip: true, NameKey: null, name, ThumbnailName: null, Factions: [], Tags: []);

    private static GameDataCatalog Catalog(params GameUnitDefinition[] definitions) => new(
        definitions.ToDictionary(definition => definition.BaseId, StringComparer.Ordinal),
        new Dictionary<string, GameSkillDefinition>(),
        []);

    private sealed class FakeCatalog(GameDataCatalog catalog) : ISwgohGameDataCatalog
    {
        public Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(catalog);
    }

    private sealed class FakePlayerProfileService(PlayerProfile player) : IPlayerProfileService
    {
        public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<PlayerProfile?>(allyCode == player.AllyCode ? player : null);

        public Task<PlayerProfile> SaveAsync(
            long allyCode,
            string name,
            long galacticPower,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlayerProfile> RefreshFromGameAsync(
            long allyCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
