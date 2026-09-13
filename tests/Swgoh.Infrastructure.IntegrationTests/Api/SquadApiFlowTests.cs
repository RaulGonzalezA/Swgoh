using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using MongoDB.Driver;

using Swgoh.Application.GameData;
using Swgoh.Infrastructure.IntegrationTests.Persistence;
using Swgoh.Infrastructure.Persistence;
using Swgoh.Infrastructure.Persistence.Documents;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Api;

[Collection(MongoDbTestGroup.Name)]
public sealed class SquadApiFlowTests(MongoDbContainerFixture fixture)
{
    [Fact]
    public async Task SquadCrud_SearchAndEnrichment_WorkEndToEndWithMongo()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var connectionStringScope = new EnvironmentVariableScope(
            "ConnectionStrings__swgoh",
            fixture.ConnectionString);
        await using var factory = new SwgohApiFactory(new FakeGameDataCatalog(CreateGameDataCatalog()));
        using HttpClient client = factory.CreateClient();

        var createRequest = new
        {
            name = "Defensa Sith",
            format = "5v5",
            use = "Defense",
            tags = new[] { " GAC ", "SITH" },
            variants = new[]
            {
                new
                {
                    key = "default",
                    name = "Principal",
                    leaderDefinitionId = "leader",
                    memberDefinitionIds = new[] { "MEMBER1", "MEMBER2", "MEMBER3", "MEMBER4" }
                }
            }
        };

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/squads",
            createRequest,
            cancellationToken);
        using JsonDocument created = await ReadJsonAsync(createResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Guid id = created.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("5v5", created.RootElement.GetProperty("format").GetString());
        Assert.Equal("Defense", created.RootElement.GetProperty("use").GetString());
        string[] tags = created.RootElement.GetProperty("tags")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        Assert.Equal(new[] { "gac", "sith" }, tags);
        JsonElement createdVariant = Assert.Single(created.RootElement.GetProperty("variants").EnumerateArray().ToArray());
        Assert.Equal("LEADER", createdVariant.GetProperty("leader").GetProperty("definitionId").GetString());
        Assert.Equal("Líder Sith", createdVariant.GetProperty("leader").GetProperty("name").GetString());
        Assert.Equal(4, createdVariant.GetProperty("members").GetArrayLength());

        using HttpResponseMessage getResponse = await client.GetAsync($"/api/v1/squads/{id}", cancellationToken);
        using JsonDocument persisted = await ReadJsonAsync(getResponse, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("Defensa Sith", persisted.RootElement.GetProperty("name").GetString());

        using HttpResponseMessage searchResponse = await client.GetAsync(
            "/api/v1/squads?format=5v5&use=Defense&tag=sith&search=Sith&limit=20",
            cancellationToken);
        using JsonDocument search = await ReadJsonAsync(searchResponse, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        Assert.Single(search.RootElement.EnumerateArray().ToArray());

        var updateRequest = new
        {
            name = "Ataque Sith",
            format = "3v3",
            use = "Offense",
            tags = new[] { "gac", "ataque" },
            variants = new[]
            {
                new
                {
                    key = "default",
                    name = "Principal",
                    leaderDefinitionId = "LEADER",
                    memberDefinitionIds = new[] { "MEMBER1", "MEMBER2" }
                }
            }
        };

        using HttpResponseMessage updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/squads/{id}",
            updateRequest,
            cancellationToken);
        using JsonDocument updated = await ReadJsonAsync(updateResponse, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("3v3", updated.RootElement.GetProperty("format").GetString());
        Assert.Equal("Offense", updated.RootElement.GetProperty("use").GetString());
        Assert.Equal(2, updated.RootElement.GetProperty("variants")[0].GetProperty("members").GetArrayLength());

        using HttpResponseMessage oldSearchResponse = await client.GetAsync(
            "/api/v1/squads?format=5v5&use=Defense&tag=sith",
            cancellationToken);
        using JsonDocument oldSearch = await ReadJsonAsync(oldSearchResponse, cancellationToken);
        Assert.Empty(oldSearch.RootElement.EnumerateArray().ToArray());

        await AssertPersistedAsync(fixture.ConnectionString, id, "Ataque Sith", cancellationToken);

        using HttpResponseMessage deleteResponse = await client.DeleteAsync($"/api/v1/squads/{id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using HttpResponseMessage missingResponse = await client.GetAsync($"/api/v1/squads/{id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WithShipInCharacterSquad_ReturnsValidationProblem()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var connectionStringScope = new EnvironmentVariableScope(
            "ConnectionStrings__swgoh",
            fixture.ConnectionString);
        await using var factory = new SwgohApiFactory(new FakeGameDataCatalog(CreateGameDataCatalog()));
        using HttpClient client = factory.CreateClient();
        var request = new
        {
            name = "Invalid",
            format = "3v3",
            use = "Flexible",
            tags = Array.Empty<string>(),
            variants = new[]
            {
                new
                {
                    key = "default",
                    name = "Principal",
                    leaderDefinitionId = "SHIP",
                    memberDefinitionIds = new[] { "MEMBER1", "MEMBER2" }
                }
            }
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/squads",
            request,
            cancellationToken);
        using JsonDocument problem = await ReadJsonAsync(response, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("squad", out JsonElement errors));
        Assert.Contains("cannot be used", errors[0].GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertPersistedAsync(
        string connectionString,
        Guid id,
        string expectedName,
        CancellationToken cancellationToken)
    {
        var mongoClient = new MongoClient(connectionString);
        IMongoDatabase database = mongoClient.GetDatabase("swgoh");
        IMongoCollection<SquadDefinitionDocument> collection = database.GetCollection<SquadDefinitionDocument>(
            SquadMongoRepository.CollectionName);
        SquadDefinitionDocument? document = await collection
            .Find(item => item.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

        Assert.NotNull(document);
        Assert.Equal(expectedName, document.Name);
        Assert.Equal(3, document.Format);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static GameDataCatalog CreateGameDataCatalog() => new(
        new Dictionary<string, GameUnitDefinition>(StringComparer.Ordinal)
        {
            ["LEADER"] = new("LEADER", false, null, "Líder Sith", "tex.leader", ["Sith"], ["affiliation_sith"]),
            ["MEMBER1"] = new("MEMBER1", false, null, "Miembro 1", "tex.member1", ["Sith"], ["affiliation_sith"]),
            ["MEMBER2"] = new("MEMBER2", false, null, "Miembro 2", "tex.member2", ["Sith"], ["affiliation_sith"]),
            ["MEMBER3"] = new("MEMBER3", false, null, "Miembro 3", null, ["Sith"], ["affiliation_sith"]),
            ["MEMBER4"] = new("MEMBER4", false, null, "Miembro 4", null, ["Sith"], ["affiliation_sith"]),
            ["SHIP"] = new("SHIP", true, null, "Nave Sith", null, ["Sith"], ["affiliation_sith"])
        },
        new Dictionary<string, GameSkillDefinition>(StringComparer.Ordinal),
        []);

    private sealed class SwgohApiFactory(ISwgohGameDataCatalog gameDataCatalog) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISwgohGameDataCatalog>();
                services.AddSingleton(gameDataCatalog);
            });
        }
    }

    private sealed class FakeGameDataCatalog(GameDataCatalog catalog) : ISwgohGameDataCatalog
    {
        public Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(catalog);
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
