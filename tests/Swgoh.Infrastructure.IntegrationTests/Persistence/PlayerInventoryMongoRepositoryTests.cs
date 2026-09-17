using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Investments;
using Swgoh.Infrastructure.Persistence;
using Swgoh.Infrastructure.Persistence.Documents;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Persistence;

[Collection(MongoDbTestGroup.Name)]
public sealed class PlayerInventoryMongoRepositoryTests(MongoDbContainerFixture fixture)
{
    [Fact]
    public async Task UpsertAsync_PersistsAndReplacesInventorySnapshot()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerInventoryMongoRepository repository = CreateRepository();
        const long allyCode = 476_825_771;
        DateTimeOffset firstCapture = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset secondCapture = firstCapture.AddHours(2);

        await repository.UpsertAsync(
            new PlayerInventorySnapshot(
                allyCode,
                firstCapture,
                "manual",
                [new PlayerInventoryResource("zinbiddle_card", "Zinbiddle Card", 8)]),
            cancellationToken);
        await repository.UpsertAsync(
            new PlayerInventorySnapshot(
                allyCode,
                secondCapture,
                "screenshot",
                [new PlayerInventoryResource("zinbiddle_card", "Zinbiddle Card", 14)]),
            cancellationToken);

        PlayerInventorySnapshot result = Assert.IsType<PlayerInventorySnapshot>(
            await repository.GetAsync(allyCode, cancellationToken));
        Assert.Equal(secondCapture, result.CapturedAtUtc);
        Assert.Equal("screenshot", result.Source);
        Assert.Equal(14, Assert.Single(result.Resources).Quantity);
    }

    [Fact]
    public async Task GetAsync_WhenPlayerHasNoSnapshot_ReturnsNull()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerInventoryMongoRepository repository = CreateRepository();

        PlayerInventorySnapshot? result = await repository.GetAsync(476_825_771, cancellationToken);

        Assert.Null(result);
    }

    private PlayerInventoryMongoRepository CreateRepository()
    {
        var client = new MongoClient(fixture.ConnectionString);
        IMongoDatabase database = client.GetDatabase($"swgoh_tests_{Guid.NewGuid():N}");
        IMongoCollection<PlayerInventoryDocument> collection = database
            .GetCollection<PlayerInventoryDocument>(PlayerInventoryMongoRepository.CollectionName);
        var genericRepository = new InventoryGenericRepository(collection);
        return new PlayerInventoryMongoRepository(genericRepository);
    }

    private sealed class InventoryGenericRepository(IMongoCollection<PlayerInventoryDocument> collection)
        : MongoDbRepository<PlayerInventoryDocument, long>(collection)
    {
        protected override FilterDefinition<PlayerInventoryDocument> GetIdFilter(long id) =>
            Builders<PlayerInventoryDocument>.Filter.Eq(document => document.AllyCode, id);

        protected override long GetId(PlayerInventoryDocument item) => item.AllyCode;
    }
}
