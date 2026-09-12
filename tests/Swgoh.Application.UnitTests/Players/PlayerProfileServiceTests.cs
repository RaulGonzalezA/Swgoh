using Swgoh.Application.Abstractions;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Players;

public sealed class PlayerProfileServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshFromGameAsync_MapsPersistsAndSnapshotsImportedPlayer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var repository = new FakePlayerRepository();
        var snapshots = new FakePlayerSnapshotRepository();
        var client = new FakePlayerClient(new ImportedPlayer(
            476_825_771,
            "player-id",
            "Aberronko",
            "guild-id",
            "Guild",
            85,
            70_000,
            [
                new ImportedRosterUnit("char-1", "CHARACTER", 85, 7, 13, 9, 6, 40_000, false, 2, 1),
                new ImportedRosterUnit("ship-1", "SHIP", 85, 7, 1, 0, 0, 30_000, true)
            ]));
        var service = new PlayerProfileService(repository, snapshots, client, new FakeClock(FixedNow));

        PlayerProfile result = await service.RefreshFromGameAsync(476_825_771, cancellationToken);

        Assert.Equal("player-id", result.PlayerId);
        Assert.Equal("Aberronko", result.Name);
        Assert.Equal("guild-id", result.GuildId);
        Assert.Equal(85, result.Level);
        Assert.Equal(70_000, result.GalacticPower);
        Assert.Equal(FixedNow, result.UpdatedAtUtc);
        Assert.Equal(2, result.Roster.Count);
        Assert.Same(result, repository.SavedPlayer);

        PlayerSnapshot snapshot = Assert.IsType<PlayerSnapshot>(snapshots.SavedSnapshot);
        Assert.Equal(70_000, snapshot.GalacticPower);
        Assert.Equal(40_000, snapshot.CharacterGalacticPower);
        Assert.Equal(30_000, snapshot.ShipGalacticPower);
        Assert.Equal(1, snapshot.CharacterCount);
        Assert.Equal(1, snapshot.ShipCount);
        Assert.Equal(2, snapshot.Zetas);
        Assert.Equal(1, snapshot.Omicrons);
        Assert.Equal(FixedNow, snapshot.CapturedAtUtc);
    }

    [Fact]
    public async Task SaveAsync_WhenPlayerDoesNotExist_CreatesAndPersistsPlayer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var repository = new FakePlayerRepository();
        var service = new PlayerProfileService(
            repository,
            new FakePlayerSnapshotRepository(),
            new FakePlayerClient(),
            new FakeClock(FixedNow));

        PlayerProfile result = await service.SaveAsync(
            476_825_771,
            "  Aberronko  ",
            13_700_000,
            cancellationToken);

        Assert.Equal("Aberronko", result.Name);
        Assert.Equal(13_700_000, result.GalacticPower);
        Assert.Equal(FixedNow, result.UpdatedAtUtc);
        Assert.Empty(result.Roster);
        Assert.Same(result, repository.SavedPlayer);
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakePlayerRepository : IPlayerRepository
    {
        public PlayerProfile? Player { get; set; }
        public PlayerProfile? SavedPlayer { get; private set; }

        public Task<PlayerProfile?> FindByAllyCodeAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Player);

        public Task UpsertAsync(PlayerProfile player, CancellationToken cancellationToken = default)
        {
            SavedPlayer = player;
            Player = player;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlayerSnapshotRepository : IPlayerSnapshotRepository
    {
        public PlayerSnapshot? SavedSnapshot { get; private set; }

        public Task UpsertAsync(PlayerSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            SavedSnapshot = snapshot;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<PlayerSnapshot>> GetRecentAsync(
            long allyCode,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<PlayerSnapshot>>([]);
    }

    private sealed class FakePlayerClient(ImportedPlayer? player = null) : ISwgohPlayerClient
    {
        public Task<ImportedPlayer> GetPlayerAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(player ?? throw new InvalidOperationException("No imported player configured for this test."));
    }
}
