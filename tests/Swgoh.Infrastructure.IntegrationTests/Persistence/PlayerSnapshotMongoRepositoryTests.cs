using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Players;
using Swgoh.Infrastructure.Persistence;
using Swgoh.Infrastructure.Persistence.Documents;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Persistence;

[Collection(MongoDbTestGroup.Name)]
public sealed class PlayerSnapshotMongoRepositoryTests(MongoDbContainerFixture fixture)
{
    [Fact]
    public async Task ExistsAsync_ReturnsWhetherSnapshotExists()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerSnapshotMongoRepository repository = CreateRepository();
        PlayerSnapshot snapshot = CreateSnapshot(
            "476825771:1",
            476_825_771,
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            10);

        Assert.False(await repository.ExistsAsync(snapshot.Id, cancellationToken));

        await repository.UpsertAsync(snapshot, cancellationToken);

        Assert.True(await repository.ExistsAsync(snapshot.Id, cancellationToken));
    }

    [Fact]
    public async Task GetRecentAsync_FiltersByPlayerOrdersDescendingAndAppliesLimit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerSnapshotMongoRepository repository = CreateRepository();
        DateTimeOffset baseline = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(CreateSnapshot("target-old", 476_825_771, baseline.AddDays(1), 10), cancellationToken);
        await repository.UpsertAsync(CreateSnapshot("other-new", 111_222_333, baseline.AddDays(5), 50), cancellationToken);
        await repository.UpsertAsync(CreateSnapshot("target-new", 476_825_771, baseline.AddDays(4), 40), cancellationToken);
        await repository.UpsertAsync(CreateSnapshot("target-middle", 476_825_771, baseline.AddDays(2), 20), cancellationToken);

        IReadOnlyCollection<PlayerSnapshot> result = await repository.GetRecentAsync(476_825_771, 2, cancellationToken);

        Assert.Collection(
            result,
            snapshot => Assert.Equal("target-new", snapshot.Id),
            snapshot => Assert.Equal("target-middle", snapshot.Id));
    }

    [Fact]
    public async Task GetRecentAsync_HonorsCancellationToken()
    {
        PlayerSnapshotMongoRepository repository = CreateRepository();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.GetRecentAsync(476_825_771, 30, cancellationSource.Token));
    }

    private PlayerSnapshotMongoRepository CreateRepository()
    {
        var client = new MongoClient(fixture.ConnectionString);
        IMongoDatabase database = client.GetDatabase($"swgoh_tests_{Guid.NewGuid():N}");
        IMongoCollection<PlayerSnapshotDocument> collection = database
            .GetCollection<PlayerSnapshotDocument>(PlayerSnapshotMongoRepository.CollectionName);
        var genericRepository = new SnapshotGenericRepository(collection);
        return new PlayerSnapshotMongoRepository(genericRepository);
    }

    private static PlayerSnapshot CreateSnapshot(
        string id,
        long allyCode,
        DateTimeOffset capturedAtUtc,
        long galacticPower) =>
        new(
            id,
            allyCode,
            capturedAtUtc,
            galacticPower,
            galacticPower,
            0,
            1,
            0,
            1,
            1,
            1,
            0,
            0,
            1,
            1);

    private sealed class SnapshotGenericRepository(IMongoCollection<PlayerSnapshotDocument> collection)
        : MongoDbRepository<PlayerSnapshotDocument, string>(collection)
    {
        protected override FilterDefinition<PlayerSnapshotDocument> GetIdFilter(string id) =>
            Builders<PlayerSnapshotDocument>.Filter.Eq(document => document.Id, id);

        protected override string GetId(PlayerSnapshotDocument item) => item.Id;
    }
}
