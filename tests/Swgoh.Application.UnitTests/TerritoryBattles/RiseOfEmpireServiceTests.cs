using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Application.TerritoryBattles;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.TerritoryBattles;

public sealed class RiseOfEmpireServiceTests
{
    private const long AllyCode = 123456789;
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_RanksMissingRelicThatCompletesRecommendedTeam()
    {
        PlayerProfile player = CreatePlayer(
            Unit("EMP1", 7), Unit("EMP2", 7), Unit("EMP3", 7), Unit("EMP4", 7), Unit("EMP5", 6));
        GameDataCatalog catalog = Catalog(
            Definition("EMP1", "Empire 1", "affiliation_empire"),
            Definition("EMP2", "Empire 2", "affiliation_empire"),
            Definition("EMP3", "Empire 3", "affiliation_empire"),
            Definition("EMP4", "Empire 4", "affiliation_empire"),
            Definition("EMP5", "Empire 5", "affiliation_empire"));
        var service = new RiseOfEmpireService(new FakePlayerProfileService(player), new FakeCatalog(catalog));

        RiseOfEmpireAnalysis? analysis = await service.GetAsync(AllyCode, TestContext.Current.CancellationToken);

        Assert.NotNull(analysis);
        RiseOfEmpirePlanetAnalysis dathomir = analysis.Phases
            .SelectMany(phase => phase.Planets)
            .Single(planet => planet.Id == "dathomir");
        RiseOfEmpireTeamRecommendation empire = dathomir.RecommendedTeams
            .Single(team => team.FactionKey == "empire");
        Assert.False(empire.Ready);
        Assert.Equal(4, empire.ReadyUnits);
        RiseOfEmpireUpgradePriority priority = Assert.Single(
            analysis.UpgradePriorities,
            item => item.DefinitionId == "EMP5");
        Assert.Equal(7, priority.TargetRelicTier);
        Assert.Equal(1, priority.RelicsMissing);
    }

    [Fact]
    public async Task GetAsync_BonusPlanetCapsReadinessUntilAccessRequirementsAreMet()
    {
        PlayerProfile player = CreatePlayer(
            Unit("J1", 7), Unit("J2", 7), Unit("J3", 7), Unit("J4", 7), Unit("J5", 7));
        GameDataCatalog catalog = Catalog(
            Definition("J1", "Jedi 1", "affiliation_jedi"),
            Definition("J2", "Jedi 2", "affiliation_jedi"),
            Definition("J3", "Jedi 3", "affiliation_jedi"),
            Definition("J4", "Jedi 4", "affiliation_jedi"),
            Definition("J5", "Jedi 5", "affiliation_jedi"));
        var service = new RiseOfEmpireService(new FakePlayerProfileService(player), new FakeCatalog(catalog));

        RiseOfEmpireAnalysis? analysis = await service.GetAsync(AllyCode, TestContext.Current.CancellationToken);

        RiseOfEmpirePlanetAnalysis zeffo = Assert.Single(
            analysis!.Phases.SelectMany(phase => phase.Planets),
            planet => planet.Id == "zeffo");
        Assert.Equal(80m, zeffo.ReadinessPercent);
        Assert.NotNull(zeffo.AccessRequirement);
        Assert.False(zeffo.AccessRequirement.Ready);
    }

    [Fact]
    public async Task GetAsync_PrioritizesMissingRelicForZeffoUnlock()
    {
        PlayerProfile player = CreatePlayer(Unit("CEREJUNDA", 6), Unit("CALKESTIS", 7));
        GameDataCatalog catalog = Catalog(
            Definition("CEREJUNDA", "Cere Junda", "unaligned_force_user"),
            Definition("CALKESTIS", "Cal Kestis", "unaligned_force_user"));
        var service = new RiseOfEmpireService(new FakePlayerProfileService(player), new FakeCatalog(catalog));

        RiseOfEmpireAnalysis? analysis = await service.GetAsync(AllyCode, TestContext.Current.CancellationToken);

        Assert.NotNull(analysis);
        RiseOfEmpireUpgradePriority cere = Assert.Single(
            analysis.UpgradePriorities,
            item => item.DefinitionId == "CEREJUNDA");
        Assert.Equal(7, cere.TargetRelicTier);
        Assert.Equal(1, cere.RelicsMissing);
        Assert.Contains("especial", cere.Reason, StringComparison.OrdinalIgnoreCase);
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

    private static RosterUnit Unit(string id, int relic) => new(
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

    private static GameUnitDefinition Definition(string id, string name, params string[] tags) => new(
        id,
        IsShip: false,
        NameKey: null,
        name,
        ThumbnailName: null,
        Factions: [],
        Tags: tags);

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
