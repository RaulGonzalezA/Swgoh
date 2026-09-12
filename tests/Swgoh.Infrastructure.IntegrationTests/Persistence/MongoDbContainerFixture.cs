using Testcontainers.MongoDb;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Persistence;

public sealed class MongoDbContainerFixture : IAsyncLifetime
{
    private readonly MongoDbContainer container = new MongoDbBuilder("mongo:8").Build();

    public string ConnectionString => container.GetConnectionString();

    public ValueTask InitializeAsync() => new(container.StartAsync());

    public ValueTask DisposeAsync() => container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class MongoDbTestGroup : ICollectionFixture<MongoDbContainerFixture>
{
    public const string Name = "SWGOH MongoDB integration tests";
}
