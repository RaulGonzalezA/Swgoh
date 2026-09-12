using Swgoh.Application.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Players;

public sealed class PlayerHistoryServiceTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(500, 365)]
    public async Task GetRecentAsync_ClampsRequestedLimit(int requestedLimit, int expectedLimit)
    {
        var repository = new CapturingSnapshotRepository();
        var service = new PlayerHistoryService(repository);

        await service.GetRecentAsync(476_825_771, requestedLimit);

        Assert.Equal(476_825_771, repository.AllyCode);
        Assert.Equal(expectedLimit, repository.Limit);
    }

    private sealed class CapturingSnapshotRepository : IPlayerSnapshotRepository
    {
        public long AllyCode { get; private set; }
        public int Limit { get; private set; }

        public Task UpsertAsync(PlayerSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyCollection<PlayerSnapshot>> GetRecentAsync(
            long allyCode,
            int limit,
            CancellationToken cancellationToken = default)
        {
            AllyCode = allyCode;
            Limit = limit;
            return Task.FromResult<IReadOnlyCollection<PlayerSnapshot>>([]);
        }
    }
}
