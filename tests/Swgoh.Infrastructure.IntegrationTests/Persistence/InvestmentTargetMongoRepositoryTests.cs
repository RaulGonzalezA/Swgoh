using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Investments;
using Swgoh.Infrastructure.Persistence;
using Swgoh.Infrastructure.Persistence.Documents;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Persistence;

[Collection(MongoDbTestGroup.Name)]
public sealed class InvestmentTargetMongoRepositoryTests(MongoDbContainerFixture fixture)
{
    [Fact]
    public async Task UpsertGetAllAndDelete_PersistsTargetLifecycle()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        InvestmentTargetMongoRepository repository = CreateRepository();
        const long allyCode = 476_825_771;
        DateTimeOffset created = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset updated = created.AddHours(1);

        await repository.UpsertAsync(
            new InvestmentTarget(allyCode, "CHAR_TEST", 7, null, created, created),
            cancellationToken);
        await repository.UpsertAsync(
            new InvestmentTarget(allyCode, "CHAR_TEST", 8, null, created, updated),
            cancellationToken);

        InvestmentTarget stored = Assert.IsType<InvestmentTarget>(
            await repository.GetAsync(allyCode, "char_test", cancellationToken));
        Assert.Equal(8, stored.TargetRelicTier);
        Assert.Equal(created, stored.CreatedAtUtc);
        Assert.Equal(updated, stored.UpdatedAtUtc);
        Assert.Single(await repository.GetAllAsync(allyCode, cancellationToken));

        Assert.True(await repository.DeleteAsync(allyCode, "CHAR_TEST", cancellationToken));
        Assert.Null(await repository.GetAsync(allyCode, "CHAR_TEST", cancellationToken));
    }

    private InvestmentTargetMongoRepository CreateRepository()
    {
        var client = new MongoClient(fixture.ConnectionString);
        IMongoDatabase database = client.GetDatabase($"swgoh_tests_{Guid.NewGuid():N}");
        IMongoCollection<InvestmentTargetDocument> collection = database
            .GetCollection<InvestmentTargetDocument>(InvestmentTargetMongoRepository.CollectionName);
        var genericRepository = new TargetGenericRepository(collection);
        return new InvestmentTargetMongoRepository(genericRepository);
    }

    private sealed class TargetGenericRepository(IMongoCollection<InvestmentTargetDocument> collection)
        : MongoDbRepository<InvestmentTargetDocument, string>(collection)
    {
        protected override FilterDefinition<InvestmentTargetDocument> GetIdFilter(string id) =>
            Builders<InvestmentTargetDocument>.Filter.Eq(document => document.Id, id);

        protected override string GetId(InvestmentTargetDocument item) => item.Id;
    }
}
