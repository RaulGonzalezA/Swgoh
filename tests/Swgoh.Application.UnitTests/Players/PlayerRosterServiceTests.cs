using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Players;

public sealed class PlayerRosterServiceTests
{
    private static readonly DateTimeOffset UpdatedAtUtc = new(2026, 9, 13, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_WhenPlayerDoesNotExist_ReturnsNull()
    {
        var service = CreateService(null);

        PlayerRosterPage? result = await service.GetAsync(
            476_825_771,
            new PlayerRosterQuery(),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_AppliesFiltersOrderingPagingAndEnrichment()
    {
        PlayerProfile player = CreatePlayer();
        var service = CreateService(player);
        var query = new PlayerRosterQuery(
            Page: 2,
            PageSize: 1,
            Search: "CHAR_",
            Type: PlayerRosterUnitType.Character,
            MinRarity: 7,
            MinRelic: 5,
            HasZeta: true,
            HasOmicron: null,
            OrderBy: PlayerRosterSortField.GalacticPower,
            Direction: PlayerRosterSortDirection.Ascending);

        PlayerRosterPage? result = await service.GetAsync(
            476_825_771,
            query,
            TestContext.Current.CancellationToken);

        PlayerRosterPage page = Assert.IsType<PlayerRosterPage>(result);
        Assert.Equal(476_825_771, page.AllyCode);
        Assert.Equal(UpdatedAtUtc, page.UpdatedAtUtc);
        Assert.Equal("Aberronko", page.PlayerName);
        Assert.Equal(4_100, page.GalacticPower);
        Assert.Equal(5, page.RosterCount);
        Assert.Contains("Empire", page.FactionOptions);
        Assert.Contains("Galactic Republic", page.FactionOptions);
        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(2, page.TotalPages);

        PlayerRosterUnit unit = Assert.Single(page.Items);
        Assert.Equal("CHAR_ALPHA", unit.DefinitionId);
        Assert.Equal("Alpha Trooper", unit.Name);
        Assert.Equal("UNIT_CHAR_ALPHA_NAME", unit.NameKey);
        Assert.Equal("tex.charui_alpha", unit.ThumbnailName);
        Assert.Contains("Galactic Republic", unit.Factions);
        Assert.Contains("affiliation_republic", unit.Tags);
        Assert.Equal(900, unit.GalacticPower);
    }

    [Fact]
    public async Task GetAsync_FiltersByExactFactionIgnoringCase()
    {
        var service = CreateService(CreatePlayer());

        PlayerRosterPage? result = await service.GetAsync(
            476_825_771,
            new PlayerRosterQuery(
                Type: PlayerRosterUnitType.Character,
                Faction: "galactic republic",
                OrderBy: PlayerRosterSortField.Name,
                Direction: PlayerRosterSortDirection.Ascending),
            TestContext.Current.CancellationToken);

        PlayerRosterPage page = Assert.IsType<PlayerRosterPage>(result);
        PlayerRosterUnit unit = Assert.Single(page.Items);
        Assert.Equal("CHAR_ALPHA", unit.DefinitionId);
        Assert.Contains("Empire", page.FactionOptions);
        Assert.Contains("Galactic Republic", page.FactionOptions);
    }

    [Fact]
    public async Task GetAsync_SearchesLocalizedNameAndFactionAndFallsBackForUnknownUnit()
    {
        var service = CreateService(CreatePlayer());

        PlayerRosterPage? factionResult = await service.GetAsync(
            476_825_771,
            new PlayerRosterQuery(
                Search: "Galactic Republic",
                Type: PlayerRosterUnitType.Character,
                OrderBy: PlayerRosterSortField.Name,
                Direction: PlayerRosterSortDirection.Ascending),
            TestContext.Current.CancellationToken);
        PlayerRosterUnit factionUnit = Assert.Single(Assert.IsType<PlayerRosterPage>(factionResult).Items);
        Assert.Equal("CHAR_ALPHA", factionUnit.DefinitionId);

        PlayerRosterPage? fallbackResult = await service.GetAsync(
            476_825_771,
            new PlayerRosterQuery(Search: "CHAR_GAMMA"),
            TestContext.Current.CancellationToken);
        PlayerRosterUnit fallbackUnit = Assert.Single(Assert.IsType<PlayerRosterPage>(fallbackResult).Items);
        Assert.Equal("CHAR_GAMMA", fallbackUnit.Name);
        Assert.Null(fallbackUnit.NameKey);
        Assert.Null(fallbackUnit.ThumbnailName);
        Assert.Empty(fallbackUnit.Factions);
        Assert.Empty(fallbackUnit.Tags);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetAsync_WhenPagingIsInvalid_Throws(int page, int pageSize)
    {
        var service = CreateService(CreatePlayer());
        var query = new PlayerRosterQuery(Page: page, PageSize: pageSize);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetAsync(476_825_771, query, TestContext.Current.CancellationToken));
    }

    private static PlayerRosterService CreateService(PlayerProfile? player)
    {
        GameDataCatalog catalog = CreateCatalog();
        return new PlayerRosterService(
            new FakePlayerRepository(player),
            new FakeGameDataCatalog(catalog),
            new FakeRosterGameDataCatalog(catalog.Units));
    }

    private static GameDataCatalog CreateCatalog() => new(
        new Dictionary<string, GameUnitDefinition>(StringComparer.Ordinal)
        {
            ["CHAR_ALPHA"] = new(
                "CHAR_ALPHA",
                false,
                "UNIT_CHAR_ALPHA_NAME",
                "Alpha Trooper",
                "tex.charui_alpha",
                ["Clone Trooper", "Galactic Republic"],
                ["profession_clonetrooper", "affiliation_republic"]),
            ["CHAR_BETA"] = new(
                "CHAR_BETA",
                false,
                "UNIT_CHAR_BETA_NAME",
                "Beta Guard",
                "tex.charui_beta",
                ["Empire"],
                ["affiliation_empire"]),
            ["SHIP_ALPHA"] = new(
                "SHIP_ALPHA",
                true,
                "UNIT_SHIP_ALPHA_NAME",
                "Alpha Fighter",
                "tex.charui_ship_alpha",
                ["Galactic Republic"],
                ["affiliation_republic", "shipclass_fighter"])
        },
        new Dictionary<string, GameSkillDefinition>(StringComparer.Ordinal),
        []);

    private static PlayerProfile CreatePlayer() => PlayerProfile.Import(
        476_825_771,
        "player-id",
        "Aberronko",
        null,
        null,
        85,
        4_100,
        UpdatedAtUtc,
        [
            new RosterUnit("char-alpha", "CHAR_ALPHA", 85, 7, 13, 8, 6, 900, false, 2, 1),
            new RosterUnit("char-beta", "CHAR_BETA", 85, 7, 13, 5, 6, 700, false, 1, 0),
            new RosterUnit("char-gamma", "CHAR_GAMMA", 85, 5, 11, 0, 4, 500, false, 0, 0),
            new RosterUnit("ship-alpha", "SHIP_ALPHA", 85, 7, 1, 0, 0, 1_100, true, 0, 0),
            new RosterUnit("ship-beta", "SHIP_BETA", 85, 6, 1, 0, 0, 900, true, 0, 0)
        ]);

    private sealed class FakeGameDataCatalog(GameDataCatalog catalog) : ISwgohGameDataCatalog
    {
        public Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(catalog);
    }

    private sealed class FakeRosterGameDataCatalog(IReadOnlyDictionary<string, GameUnitDefinition> units)
        : IRosterGameDataCatalog
    {
        public Task<IReadOnlyDictionary<string, GameUnitDefinition>> GetUnitsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(units);
    }

    private sealed class FakePlayerRepository(PlayerProfile? player) : IPlayerRepository
    {
        public Task<PlayerProfile?> FindByAllyCodeAsync(
            long allyCode,
            CancellationToken cancellationToken = default) => Task.FromResult(player);

        public Task UpsertAsync(PlayerProfile playerProfile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
