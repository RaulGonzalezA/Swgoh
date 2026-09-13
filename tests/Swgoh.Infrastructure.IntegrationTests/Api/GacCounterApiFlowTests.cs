using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Swgoh.Application.GameData;
using Swgoh.Infrastructure.IntegrationTests.Persistence;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Api;

[Collection(MongoDbTestGroup.Name)]
public sealed class GacCounterApiFlowTests(MongoDbContainerFixture fixture)
{
    [Fact]
    public async Task ImportHistory_ThenGetCounters_ReturnsObservedExactTeamStatistics()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var connectionStringScope = new EnvironmentVariableScope(
            "ConnectionStrings__swgoh",
            fixture.ConnectionString);
        await using var factory = new SwgohApiFactory(new FakeGameDataCatalog(CreateGameDataCatalog()));
        using HttpClient client = factory.CreateClient();

        DateTimeOffset start = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
        object Defender() => new
        {
            leaderDefinitionId = "DEF",
            memberDefinitionIds = new[] { "DEF2", "DEF3" },
            isFleet = false
        };
        object Attacker() => new
        {
            leaderDefinitionId = "ATK",
            memberDefinitionIds = new[] { "ATK2", "ATK3" },
            isFleet = false
        };

        var importRequest = new
        {
            rounds = new object[]
            {
                new
                {
                    season = 83,
                    eventNumber = 1,
                    roundNumber = 1,
                    format = "3v3",
                    league = "Kyber",
                    startedAtUtc = start,
                    fullClear = true,
                    source = "counter-api-test",
                    defenses = Array.Empty<object>(),
                    offenseBattles = new[]
                    {
                        new
                        {
                            zone = "front",
                            defender = Defender(),
                            attacker = Attacker(),
                            won = true,
                            banners = 57,
                            attempt = 1,
                            attackedAtUtc = start.AddMinutes(30)
                        }
                    }
                },
                new
                {
                    season = 83,
                    eventNumber = 1,
                    roundNumber = 2,
                    format = "3v3",
                    league = "Kyber",
                    startedAtUtc = start.AddDays(2),
                    fullClear = false,
                    source = "counter-api-test",
                    defenses = Array.Empty<object>(),
                    offenseBattles = new[]
                    {
                        new
                        {
                            zone = "front",
                            defender = Defender(),
                            attacker = Attacker(),
                            won = false,
                            banners = 0,
                            attempt = 2,
                            attackedAtUtc = start.AddDays(2).AddMinutes(45)
                        }
                    }
                }
            }
        };

        using HttpResponseMessage importResponse = await client.PostAsJsonAsync(
            "/api/v1/gac/opponents/333333333/history",
            importRequest,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);

        using HttpResponseMessage countersResponse = await client.GetAsync(
            "/api/v1/gac/counters?format=3v3&defenderLeader=DEF&isFleet=false&maxRounds=100&limit=10",
            cancellationToken);
        using JsonDocument counters = await ReadJsonAsync(countersResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, countersResponse.StatusCode);
        JsonElement counter = Assert.Single(counters.RootElement.EnumerateArray().ToArray());
        Assert.Equal("DEF", counter.GetProperty("defenderLeaderDefinitionId").GetString());
        Assert.Equal("ATK", counter.GetProperty("attackerLeaderDefinitionId").GetString());
        Assert.Equal(2, counter.GetProperty("uses").GetInt32());
        Assert.Equal(1, counter.GetProperty("wins").GetInt32());
        Assert.Equal(50m, counter.GetProperty("winRate").GetDecimal());
        Assert.Equal(50m, counter.GetProperty("oneShotRate").GetDecimal());
        Assert.Equal(28.5m, counter.GetProperty("averageBanners").GetDecimal());
        Assert.Equal(1, counter.GetProperty("playersObserved").GetInt32());
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static GameDataCatalog CreateGameDataCatalog()
    {
        string[] ids = ["DEF", "DEF2", "DEF3", "ATK", "ATK2", "ATK3"];
        Dictionary<string, GameUnitDefinition> units = ids.ToDictionary(
            id => id,
            id => new GameUnitDefinition(id, false, null, id, null, [], []),
            StringComparer.Ordinal);
        return new GameDataCatalog(units, new Dictionary<string, GameSkillDefinition>(StringComparer.Ordinal), []);
    }

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
