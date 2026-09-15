using Microsoft.Extensions.Configuration;

using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Persistence;
using Swgoh.Infrastructure.Persistence.Documents;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Persistence;

[Collection(MongoDbTestGroup.Name)]
public sealed class GacRoundPlanMongoConcurrencyTests(MongoDbContainerFixture fixture)
{
    [Fact]
    public async Task TrySaveAsync_WithSameExpectedVersion_AllowsExactlyOneConcurrentWriter()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        GacRoundPlanMongoRepository repository = CreateRepository();
        DateTimeOffset now = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        GacRoundPlan original = CreatePlan(now);

        Assert.True(await repository.TrySaveAsync(original, 0, cancellationToken));
        Assert.Equal(1, await repository.GetVersionAsync(original.Id, cancellationToken));

        GacRoundPlan first = await repository.FindByIdAsync(original.Id, cancellationToken)
            ?? throw new InvalidOperationException("Seeded round plan was not found.");
        GacRoundPlan second = await repository.FindByIdAsync(original.Id, cancellationToken)
            ?? throw new InvalidOperationException("Seeded round plan was not found.");
        first.Replace([], [], [], now.AddSeconds(1));
        second.Replace([], [], [], now.AddSeconds(2));

        bool[] results = await Task.WhenAll(
            repository.TrySaveAsync(first, 1, cancellationToken),
            repository.TrySaveAsync(second, 1, cancellationToken));

        Assert.Single(results.Where(saved => saved));
        Assert.Single(results.Where(saved => !saved));
        Assert.Equal(2, await repository.GetVersionAsync(original.Id, cancellationToken));
    }

    [Fact]
    public async Task TrySaveAsync_WithStaleVersion_DoesNotOverwriteLatestPlan()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        GacRoundPlanMongoRepository repository = CreateRepository();
        DateTimeOffset now = new(2026, 9, 15, 11, 0, 0, TimeSpan.Zero);
        GacRoundPlan original = CreatePlan(now);

        Assert.True(await repository.TrySaveAsync(original, 0, cancellationToken));
        original.Replace([], [], [], now.AddSeconds(1));
        Assert.True(await repository.TrySaveAsync(original, 1, cancellationToken));

        original.Replace([], [], [], now.AddSeconds(2));
        Assert.False(await repository.TrySaveAsync(original, 1, cancellationToken));
        Assert.Equal(2, await repository.GetVersionAsync(original.Id, cancellationToken));
    }

    private GacRoundPlanMongoRepository CreateRepository()
    {
        var client = new MongoClient(fixture.ConnectionString);
        IMongoDatabase database = client.GetDatabase("swgoh");
        IMongoCollection<GacRoundPlanDocument> collection = database
            .GetCollection<GacRoundPlanDocument>(GacRoundPlanMongoRepository.CollectionName);
        var genericRepository = new RoundPlanGenericRepository(collection);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:swgoh"] = fixture.ConnectionString
            })
            .Build();
        return new GacRoundPlanMongoRepository(
            genericRepository,
            new GacPlannerWriteContext(),
            configuration);
    }

    private static GacRoundPlan CreatePlan(DateTimeOffset now) => GacRoundPlan.Create(
        476_825_771,
        987_654_321,
        "phase5-event",
        $"phase5-instance-{Guid.NewGuid():N}",
        1,
        GacFormat.FiveVsFive,
        GacLeague.Kyber,
        now);

    private sealed class RoundPlanGenericRepository(IMongoCollection<GacRoundPlanDocument> collection)
        : MongoDbRepository<GacRoundPlanDocument, string>(collection)
    {
        protected override FilterDefinition<GacRoundPlanDocument> GetIdFilter(string id) =>
            Builders<GacRoundPlanDocument>.Filter.Eq(document => document.Id, id);

        protected override string GetId(GacRoundPlanDocument item) => item.Id;
    }
}
