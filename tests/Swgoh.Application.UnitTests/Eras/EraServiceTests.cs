using Swgoh.Application.Eras;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Eras;

public sealed class EraServiceTests
{
    private const long AllyCode = 123456789;
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetCurrentAsync_ListsCurrentEraUnitsAndMatchesByGameDataName()
    {
        PlayerProfile player = CreatePlayer(
            Character("UNIT_MARA_ERA", 5, 7),
            Character("UNIT_RONIN_ERA", 3, 7));
        GameDataCatalog catalog = Catalog(
            Definition("UNIT_MARA_ERA", "Mara Jade Skywalker"),
            Definition("UNIT_RONIN_ERA", "The Ronin"));
        var service = new EraService(new FakePlayerProfileService(player), new FakeCatalog(catalog));

        EraAnalysis? analysis = await service.GetCurrentAsync(AllyCode, TestContext.Current.CancellationToken);

        Assert.NotNull(analysis);
        Assert.Equal("Era of Myths & Legends", analysis.EraName);
        Assert.Equal(2, analysis.OwnedUnits);
        Assert.Equal(7, analysis.TotalUnits);
        EraUnitStatus mara = Assert.Single(analysis.Units, unit => unit.Name == "Mara Jade Skywalker");
        EraUnitStatus ronin = Assert.Single(analysis.Units, unit => unit.Name == "The Ronin");
        Assert.True(mara.Owned);
        Assert.Equal(5, mara.RelicTier);
        Assert.True(ronin.Owned);
        Assert.Equal("UNIT_RONIN_ERA", ronin.DefinitionId);
    }

    [Fact]
    public async Task GetCurrentAsync_ComputesJourneyStarReadinessButKeepsEraLevelSeparate()
    {
        PlayerProfile player = CreatePlayer(
            Character("MARA", 0, 4),
            Character("YODA_DSV", 0, 4),
            Character("STARKILLER_CONCEPT", 0, 4),
            Character("STORMTROOPER_CONCEPT", 0, 4),
            Character("JAXXON", 0, 4));
        GameDataCatalog catalog = Catalog(
            Definition("MARA", "Mara Jade Skywalker"),
            Definition("YODA_DSV", "Yoda (Dark Side Vision)"),
            Definition("STARKILLER_CONCEPT", "Starkiller (Luke Concept)"),
            Definition("STORMTROOPER_CONCEPT", "Stormtrooper (Concept)"),
            Definition("JAXXON", "Jaxxon"));
        var service = new EraService(new FakePlayerProfileService(player), new FakeCatalog(catalog));

        EraAnalysis? analysis = await service.GetCurrentAsync(AllyCode, TestContext.Current.CancellationToken);

        Assert.NotNull(analysis);
        EraJourneyTierProgress tierOne = Assert.Single(analysis.Journey.Tiers, tier => tier.Tier == 1);
        Assert.True(tierOne.StarRequirementsMet);
        EraLevelRequirement yoda = Assert.Single(tierOne.EraLevelRequirements);
        Assert.Equal("Yoda (Dark Side Vision)", yoda.UnitName);
        Assert.Equal(90, yoda.EraLevel);
        Assert.False(analysis.Coliseum.EraLevelIsAvailableFromRoster);
    }

    [Fact]
    public async Task GetCurrentAsync_ExposesCurrentColiseumBossesAndTierGuidance()
    {
        PlayerProfile player = CreatePlayer();
        var service = new EraService(new FakePlayerProfileService(player), new FakeCatalog(Catalog()));

        EraAnalysis? analysis = await service.GetCurrentAsync(AllyCode, TestContext.Current.CancellationToken);

        Assert.NotNull(analysis);
        Assert.Equal(12, analysis.Coliseum.MaxTier);
        Assert.Contains(analysis.Coliseum.Bosses, boss => boss.Name == "Krayt Dragon");
        Assert.Contains(analysis.Coliseum.Bosses, boss => boss.Name == "Zeffo Tomb Guardians");
        Assert.Contains(analysis.Coliseum.Bosses, boss => boss.Name == "Jotaz");
        Assert.Contains(analysis.Coliseum.Bosses, boss => boss.Name == "Dryax");
        Assert.Equal(60, Assert.Single(analysis.Coliseum.TierGuidance, tier => tier.Tier == 6).RecommendedEraLevel);
        Assert.Equal(90, Assert.Single(analysis.Coliseum.TierGuidance, tier => tier.Tier == 9).RecommendedEraLevel);
        Assert.Null(Assert.Single(analysis.Coliseum.TierGuidance, tier => tier.Tier == 12).RecommendedEraLevel);
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

    private static RosterUnit Character(string id, int relic, int rarity) => new(
        id,
        id,
        85,
        rarity,
        13,
        relic,
        6,
        25_000 + relic * 1_000,
        IsShip: false,
        ZetaCount: 1,
        OmicronCount: 0,
        Stats: new RosterUnitStats(Speed: 250 + relic));

    private static GameUnitDefinition Definition(string id, string name) => new(
        id,
        IsShip: false,
        NameKey: null,
        name,
        ThumbnailName: null,
        Factions: [],
        Tags: []);

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
