using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using MongoDB.Bson;
using MongoDB.Driver;

using Swgoh.Application.Players;
using Swgoh.Infrastructure.IntegrationTests.Persistence;
using Swgoh.Infrastructure.Persistence;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Api;

[Collection(MongoDbTestGroup.Name)]
public sealed class PlayerApiFlowTests(MongoDbContainerFixture fixture)
{
    private const long AllyCode = 476_825_771;

    [Fact]
    public async Task Refresh_PersistsPlayerAndSnapshot_ThenExposesRosterAnalysisAndHistory()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var connectionStringScope = new EnvironmentVariableScope(
            "ConnectionStrings__swgoh",
            fixture.ConnectionString);
        var provider = new FakePlayerClient(CreateImportedPlayer());
        await using var factory = new SwgohApiFactory(provider);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage refreshResponse = await client.PostAsync(
            $"/api/v1/players/{AllyCode}/refresh",
            content: null,
            cancellationToken);
        using JsonDocument refreshed = await ReadJsonAsync(refreshResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.Equal(AllyCode, refreshed.RootElement.GetProperty("allyCode").GetInt64());
        Assert.Equal("Aberronko", refreshed.RootElement.GetProperty("name").GetString());
        Assert.Equal(100_000, refreshed.RootElement.GetProperty("galacticPower").GetInt64());
        Assert.Equal(2, refreshed.RootElement.GetProperty("rosterCount").GetInt32());

        using HttpResponseMessage playerResponse = await client.GetAsync(
            $"/api/v1/players/{AllyCode}",
            cancellationToken);
        using JsonDocument persisted = await ReadJsonAsync(playerResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, playerResponse.StatusCode);
        Assert.Equal("player-id", persisted.RootElement.GetProperty("playerId").GetString());
        Assert.Equal(2, persisted.RootElement.GetProperty("roster").GetArrayLength());

        using HttpResponseMessage rosterResponse = await client.GetAsync(
            $"/api/v1/players/{AllyCode}/roster?type=Character&minRarity=7&minRelic=8&hasZeta=true&orderBy=GalacticPower&direction=Descending&page=1&pageSize=1",
            cancellationToken);
        using JsonDocument roster = await ReadJsonAsync(rosterResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, rosterResponse.StatusCode);
        Assert.Equal(1, roster.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, roster.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, roster.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, roster.RootElement.GetProperty("totalPages").GetInt32());
        JsonElement rosterUnit = Assert.Single(roster.RootElement.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal("CHARACTER", rosterUnit.GetProperty("definitionId").GetString());
        Assert.Equal(60_000, rosterUnit.GetProperty("galacticPower").GetInt64());
        Assert.False(rosterUnit.GetProperty("isShip").GetBoolean());
        Assert.Equal(2, rosterUnit.GetProperty("zetaCount").GetInt32());
        Assert.Equal(1, rosterUnit.GetProperty("omicronCount").GetInt32());

        using HttpResponseMessage analysisResponse = await client.GetAsync(
            $"/api/v1/players/{AllyCode}/analysis",
            cancellationToken);
        using JsonDocument analysis = await ReadJsonAsync(analysisResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, analysisResponse.StatusCode);
        Assert.Equal(100_000, analysis.RootElement.GetProperty("galacticPower").GetInt64());
        Assert.Equal(60_000, analysis.RootElement.GetProperty("characterGalacticPower").GetInt64());
        Assert.Equal(40_000, analysis.RootElement.GetProperty("shipGalacticPower").GetInt64());
        Assert.Equal(1, analysis.RootElement.GetProperty("characterCount").GetInt32());
        Assert.Equal(1, analysis.RootElement.GetProperty("shipCount").GetInt32());
        Assert.Equal(2, analysis.RootElement.GetProperty("zetas").GetInt32());
        Assert.Equal(1, analysis.RootElement.GetProperty("omicrons").GetInt32());

        using HttpResponseMessage historyResponse = await client.GetAsync(
            $"/api/v1/players/{AllyCode}/history?limit=5",
            cancellationToken);
        using JsonDocument history = await ReadJsonAsync(historyResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        JsonElement snapshot = Assert.Single(history.RootElement.EnumerateArray().ToArray());
        Assert.Equal(AllyCode, snapshot.GetProperty("allyCode").GetInt64());
        Assert.Equal(100_000, snapshot.GetProperty("galacticPower").GetInt64());
        Assert.Equal(2, snapshot.GetProperty("zetas").GetInt32());
        Assert.Equal(1, snapshot.GetProperty("omicrons").GetInt32());
        Assert.Equal(1, provider.CallCount);

        await AssertSnapshotPersistenceAndIndexAsync(fixture.ConnectionString, cancellationToken);
    }

    private static async Task AssertSnapshotPersistenceAndIndexAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var client = new MongoClient(connectionString);
        IMongoDatabase database = client.GetDatabase("swgoh");
        IMongoCollection<BsonDocument> collection = database.GetCollection<BsonDocument>(
            PlayerSnapshotMongoRepository.CollectionName);

        long snapshotCount = await collection.CountDocumentsAsync(
            new BsonDocument("AllyCode", AllyCode),
            cancellationToken: cancellationToken);
        Assert.Equal(1, snapshotCount);

        using IAsyncCursor<BsonDocument> cursor = await collection.Indexes.ListAsync(cancellationToken);
        List<BsonDocument> indexes = await cursor.ToListAsync(cancellationToken);
        BsonDocument compoundIndex = Assert.Single(
            indexes,
            index => index.TryGetValue("name", out BsonValue? name)
                && name.IsString
                && string.Equals(
                    name.AsString,
                    PlayerSnapshotMongoRepository.AllyCodeCapturedAtIndexName,
                    StringComparison.Ordinal));

        BsonDocument keys = compoundIndex["key"].AsBsonDocument;
        Assert.Equal(1, keys["AllyCode"].ToInt32());
        Assert.Equal(-1, keys["CapturedAtUtc"].ToInt32());
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static ImportedPlayer CreateImportedPlayer() => new(
        AllyCode,
        "player-id",
        "Aberronko",
        "guild-id",
        "Guild",
        85,
        100_000,
        [
            new ImportedRosterUnit(
                "character-id",
                "CHARACTER",
                85,
                7,
                13,
                9,
                6,
                60_000,
                IsShip: false,
                ZetaCount: 2,
                OmicronCount: 1),
            new ImportedRosterUnit(
                "ship-id",
                "SHIP",
                85,
                7,
                1,
                0,
                0,
                40_000,
                IsShip: true)
        ]);

    private sealed class SwgohApiFactory(ISwgohPlayerClient playerClient) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISwgohPlayerClient>();
                services.AddSingleton(playerClient);
            });
        }
    }

    private sealed class FakePlayerClient(ImportedPlayer player) : ISwgohPlayerClient
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public Task<ImportedPlayer> GetPlayerAsync(
            long allyCode,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            Assert.Equal(player.AllyCode, allyCode);
            return Task.FromResult(player);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string name;
        private readonly string? previousValue;

        public EnvironmentVariableScope(string name, string value)
        {
            this.name = name;
            previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(name, previousValue);
    }
}
