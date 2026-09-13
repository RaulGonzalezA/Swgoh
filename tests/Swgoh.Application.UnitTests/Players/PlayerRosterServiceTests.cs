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
        var service = new PlayerRosterService(new FakePlayerRepository(null));

        PlayerRosterPage? result = await service.GetAsync(
            476_825_771,
            new PlayerRosterQuery(),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_AppliesFiltersOrderingAndPaging()
    {
        PlayerProfile player = CreatePlayer();
        var service = new PlayerRosterService(new FakePlayerRepository(player));
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
        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(2, page.TotalPages);

        RosterUnit unit = Assert.Single(page.Items);
        Assert.Equal("CHAR_ALPHA", unit.DefinitionId);
        Assert.Equal(900, unit.GalacticPower);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetAsync_WhenPagingIsInvalid_Throws(int page, int pageSize)
    {
        var service = new PlayerRosterService(new FakePlayerRepository(CreatePlayer()));
        var query = new PlayerRosterQuery(Page: page, PageSize: pageSize);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetAsync(476_825_771, query, TestContext.Current.CancellationToken));
    }

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

    private sealed class FakePlayerRepository(PlayerProfile? player) : IPlayerRepository
    {
        public Task<PlayerProfile?> FindByAllyCodeAsync(
            long allyCode,
            CancellationToken cancellationToken = default) => Task.FromResult(player);

        public Task UpsertAsync(PlayerProfile playerProfile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
