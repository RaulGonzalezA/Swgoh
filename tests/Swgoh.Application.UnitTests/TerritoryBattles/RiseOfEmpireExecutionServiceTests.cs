using Swgoh.Application.Abstractions;
using Swgoh.Application.Players;
using Swgoh.Application.TerritoryBattles;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.TerritoryBattles;

public sealed class RiseOfEmpireExecutionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAsync_CreatesOneActiveSessionPerGuild()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerProfile player = Player();
        var repository = new FakeRepository();
        var service = new RiseOfEmpireExecutionService(new FakePlayerService(player), repository, new FixedClock(Now));

        RiseOfEmpireExecutionSession first = await service.StartAsync(player.AllyCode, "TB septiembre", cancellationToken);
        RiseOfEmpireExecutionSession second = await service.StartAsync(player.AllyCode, "Otra", cancellationToken);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("TB septiembre", first.Label);
        Assert.Equal(RiseOfEmpireExecutionStatus.Active, first.Status);
        Assert.Single(repository.Sessions);
    }

    [Fact]
    public async Task UpdateMissionAsync_StoresWavesTerritoryPointsAndReplacesSameMission()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerProfile player = Player();
        var repository = new FakeRepository();
        var service = new RiseOfEmpireExecutionService(new FakePlayerService(player), repository, new FixedClock(Now));
        RiseOfEmpireExecutionSession session = await service.StartAsync(player.AllyCode, null, cancellationToken);

        RiseOfEmpireMissionResultCommand command = Command(player, completedWaves: 1, territoryPoints: 2_500_000);
        await service.UpdateMissionAsync(player.AllyCode, session.Id, command, cancellationToken);
        RiseOfEmpireExecutionSession updated = await service.UpdateMissionAsync(
            player.AllyCode,
            session.Id,
            command with { CompletedWaves = 2, TerritoryPoints = 5_000_000 },
            cancellationToken);

        RiseOfEmpireMissionExecutionResult result = Assert.Single(updated.Results);
        Assert.Equal(2, result.CompletedWaves);
        Assert.Equal(2, result.TotalWaves);
        Assert.Equal(5_000_000, result.TerritoryPoints);
        Assert.Equal(1, updated.FinishedAttempts);
        Assert.Equal(5_000_000, updated.RecordedTerritoryPoints);
    }

    [Fact]
    public async Task CloseAsync_RemovesSessionFromActiveButKeepsHistory()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerProfile player = Player();
        var repository = new FakeRepository();
        var service = new RiseOfEmpireExecutionService(new FakePlayerService(player), repository, new FixedClock(Now));
        RiseOfEmpireExecutionSession session = await service.StartAsync(player.AllyCode, "Actual", cancellationToken);

        RiseOfEmpireExecutionSession closed = await service.CloseAsync(player.AllyCode, session.Id, cancellationToken);
        RiseOfEmpireExecutionSession? active = await service.GetActiveAsync(player.AllyCode, cancellationToken);
        IReadOnlyCollection<RiseOfEmpireExecutionSession> history = await service.GetHistoryAsync(player.AllyCode, cancellationToken);

        Assert.Equal(RiseOfEmpireExecutionStatus.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAtUtc);
        Assert.Null(active);
        Assert.Contains(history, item => item.Id == session.Id && item.Status == RiseOfEmpireExecutionStatus.Closed);
    }

    private static RiseOfEmpireMissionResultCommand Command(
        PlayerProfile player,
        int completedWaves,
        long territoryPoints) => new(
        player.AllyCode,
        player.Name,
        1,
        "mustafar",
        "Mustafar",
        "mustafar-dark",
        "Combates Dark Side",
        "SLKR First Order",
        RiseOfEmpireMissionExecutionState.Finished,
        completedWaves,
        2,
        territoryPoints,
        "resultado real");

    private static PlayerProfile Player() => PlayerProfile.Import(
        123_456_789,
        "player-1",
        "Raul",
        "guild-1",
        "Guild",
        85,
        10_000_000,
        Now,
        []);

    private sealed class FakeRepository : IRiseOfEmpireExecutionRepository
    {
        public List<RiseOfEmpireExecutionSession> Sessions { get; } = [];

        public Task<RiseOfEmpireExecutionSession?> GetActiveAsync(
            string guildId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            Sessions
                .Where(session => session.GuildId == guildId && session.Status == RiseOfEmpireExecutionStatus.Active)
                .OrderByDescending(session => session.UpdatedAtUtc)
                .FirstOrDefault());

        public Task<RiseOfEmpireExecutionSession?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default) => Task.FromResult(
            Sessions.FirstOrDefault(session => session.Id == id));

        public Task<IReadOnlyCollection<RiseOfEmpireExecutionSession>> GetHistoryAsync(
            string guildId,
            int limit,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<RiseOfEmpireExecutionSession>>(
            [.. Sessions.Where(session => session.GuildId == guildId).OrderByDescending(session => session.CreatedAtUtc).Take(limit)]);

        public Task UpsertAsync(RiseOfEmpireExecutionSession session, CancellationToken cancellationToken = default)
        {
            Sessions.RemoveAll(item => item.Id == session.Id);
            Sessions.Add(session);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlayerService(PlayerProfile player) : IPlayerProfileService
    {
        public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<PlayerProfile?>(allyCode == player.AllyCode ? player : null);

        public Task<PlayerProfile> SaveAsync(
            long allyCode,
            string name,
            long galacticPower,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlayerProfile> RefreshFromGameAsync(long allyCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
