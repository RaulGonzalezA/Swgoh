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
public sealed class GacOpponentScoutApiFlowTests(MongoDbContainerFixture fixture)
{
    [Fact]
    public async Task ImportHistory_AndScoutOpponent_WorkEndToEndWithMongo()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var connectionStringScope = new EnvironmentVariableScope(
            "ConnectionStrings__swgoh",
            fixture.ConnectionString);
        await using var factory = new SwgohApiFactory(new FakeGameDataCatalog(CreateGameDataCatalog()));
        using HttpClient client = factory.CreateClient();

        var squadRequest = new
        {
            name = "Defensa Sith",
            format = "5v5",
            use = "Defense",
            tags = new[] { "gac", "sith" },
            variants = new[]
            {
                new
                {
                    key = "default",
                    name = "Principal",
                    leaderDefinitionId = "DEFLEADER",
                    memberDefinitionIds = new[] { "D1", "D2", "D3", "D4" }
                }
            }
        };
        using HttpResponseMessage squadResponse = await client.PostAsJsonAsync(
            "/api/v1/squads",
            squadRequest,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, squadResponse.StatusCode);

        DateTimeOffset start = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);
        object DefenseSquad() => new
        {
            leaderDefinitionId = "defleader",
            memberDefinitionIds = new[] { "d1", "d2", "d3", "d4" },
            isFleet = false
        };
        object EnemySquad() => new
        {
            leaderDefinitionId = "ENEMYLEADER",
            memberDefinitionIds = new[] { "E1", "E2", "E3", "E4" },
            isFleet = false
        };
        object CounterSquad() => new
        {
            leaderDefinitionId = "COUNTERLEADER",
            memberDefinitionIds = new[] { "C1", "C2", "C3", "C4" },
            isFleet = false
        };

        var importRequest = new
        {
            rounds = new object[]
            {
                new
                {
                    season = 82,
                    eventNumber = 1,
                    roundNumber = 1,
                    format = "5v5",
                    league = "Aurodium",
                    startedAtUtc = start,
                    fullClear = true,
                    source = "fixture",
                    defenses = new[]
                    {
                        new { zone = "front-top", squad = DefenseSquad(), holds = 2, defeated = true }
                    },
                    offenseBattles = new[]
                    {
                        new
                        {
                            zone = "front-bottom",
                            defender = EnemySquad(),
                            attacker = CounterSquad(),
                            won = true,
                            banners = 65,
                            attempt = 1,
                            attackedAtUtc = start.AddMinutes(60)
                        }
                    }
                },
                new
                {
                    season = 82,
                    eventNumber = 1,
                    roundNumber = 2,
                    format = "5v5",
                    league = "Aurodium",
                    startedAtUtc = start.AddDays(2),
                    fullClear = true,
                    source = "fixture",
                    defenses = new[]
                    {
                        new { zone = "front-top", squad = DefenseSquad(), holds = 0, defeated = true }
                    },
                    offenseBattles = new[]
                    {
                        new
                        {
                            zone = "front-bottom",
                            defender = EnemySquad(),
                            attacker = CounterSquad(),
                            won = true,
                            banners = 55,
                            attempt = 2,
                            attackedAtUtc = start.AddDays(2).AddMinutes(120)
                        }
                    }
                },
                new
                {
                    season = 82,
                    eventNumber = 1,
                    roundNumber = 3,
                    format = "5v5",
                    league = "Aurodium",
                    startedAtUtc = start.AddDays(4),
                    fullClear = false,
                    source = "fixture",
                    defenses = Array.Empty<object>(),
                    offenseBattles = new[]
                    {
                        new
                        {
                            zone = "front-bottom",
                            defender = EnemySquad(),
                            attacker = CounterSquad(),
                            won = false,
                            banners = 0,
                            attempt = 1,
                            attackedAtUtc = start.AddDays(4).AddMinutes(180)
                        }
                    }
                }
            }
        };

        using HttpResponseMessage importResponse = await client.PostAsJsonAsync(
            "/api/v1/gac/opponents/123456789/history",
            importRequest,
            cancellationToken);
        using JsonDocument imported = await ReadJsonAsync(importResponse, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.Equal(3, imported.RootElement.GetProperty("importedRounds").GetInt32());

        using HttpResponseMessage secondImportResponse = await client.PostAsJsonAsync(
            "/api/v1/gac/opponents/123456789/history",
            importRequest,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, secondImportResponse.StatusCode);

        using HttpResponseMessage historyResponse = await client.GetAsync(
            "/api/v1/gac/opponents/123456789/history?format=5v5&maxRounds=30",
            cancellationToken);
        using JsonDocument history = await ReadJsonAsync(historyResponse, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal(3, history.RootElement.GetArrayLength());
        Assert.Equal(3, history.RootElement[0].GetProperty("roundNumber").GetInt32());

        using HttpResponseMessage scoutResponse = await client.GetAsync(
            "/api/v1/gac/opponents/123456789/scouting?format=5v5&targetLeague=Kyber&maxRounds=30",
            cancellationToken);
        using JsonDocument scout = await ReadJsonAsync(scoutResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, scoutResponse.StatusCode);
        Assert.Equal(3, scout.RootElement.GetProperty("roundsAnalyzed").GetInt32());
        Assert.Equal("Aurodium", scout.RootElement.GetProperty("latestObservedLeague").GetString());
        Assert.Equal("Kyber", scout.RootElement.GetProperty("targetLeague").GetString());
        Assert.Equal(11, scout.RootElement.GetProperty("requiredSquadDefenses").GetInt32());
        Assert.Equal(3, scout.RootElement.GetProperty("requiredFleetDefenses").GetInt32());
        Assert.Equal(2, scout.RootElement.GetProperty("additionalUnobservedSquadSlots").GetInt32());
        Assert.Equal(1, scout.RootElement.GetProperty("additionalUnobservedFleetSlots").GetInt32());
        Assert.Equal(66.7m, scout.RootElement.GetProperty("fullClearRate").GetDecimal());
        Assert.Equal(120m, scout.RootElement.GetProperty("averageFirstAttackDelayMinutes").GetDecimal());

        JsonElement defensePattern = Assert.Single(
            scout.RootElement.GetProperty("defensePatterns").EnumerateArray().ToArray());
        Assert.Equal("Líder defensa", defensePattern.GetProperty("leader").GetProperty("name").GetString());
        Assert.Equal("Defensa Sith", defensePattern.GetProperty("squadDefinitionName").GetString());
        Assert.Equal("Principal", defensePattern.GetProperty("variantName").GetString());
        Assert.Equal(2, defensePattern.GetProperty("roundsPlaced").GetInt32());
        Assert.Equal(66.7m, defensePattern.GetProperty("placementRate").GetDecimal());
        Assert.Equal(1m, defensePattern.GetProperty("averageHolds").GetDecimal());
        Assert.Equal(50m, defensePattern.GetProperty("holdRate").GetDecimal());
        Assert.Equal("Medium", defensePattern.GetProperty("confidence").GetString());

        JsonElement counterPattern = Assert.Single(
            scout.RootElement.GetProperty("counterPatterns").EnumerateArray().ToArray());
        Assert.Equal(3, counterPattern.GetProperty("uses").GetInt32());
        Assert.Equal(2, counterPattern.GetProperty("wins").GetInt32());
        Assert.Equal(66.7m, counterPattern.GetProperty("winRate").GetDecimal());
        Assert.Equal(1, counterPattern.GetProperty("oneShots").GetInt32());
        Assert.Equal(33.3m, counterPattern.GetProperty("oneShotRate").GetDecimal());

        JsonElement prediction = Assert.Single(
            scout.RootElement.GetProperty("predictedSquadDefenses").EnumerateArray().ToArray());
        Assert.Equal(66.7m, prediction.GetProperty("probability").GetDecimal());
        Assert.Equal("Defensa Sith", prediction.GetProperty("squadDefinitionName").GetString());
    }

    [Fact]
    public async Task ImportHistory_WithUnknownUnit_ReturnsValidationProblem()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var connectionStringScope = new EnvironmentVariableScope(
            "ConnectionStrings__swgoh",
            fixture.ConnectionString);
        await using var factory = new SwgohApiFactory(new FakeGameDataCatalog(CreateGameDataCatalog()));
        using HttpClient client = factory.CreateClient();

        var request = new
        {
            rounds = new[]
            {
                new
                {
                    season = 82,
                    eventNumber = 1,
                    roundNumber = 1,
                    format = "5v5",
                    league = "Kyber",
                    startedAtUtc = DateTimeOffset.UtcNow,
                    fullClear = (bool?)null,
                    source = "fixture",
                    defenses = new[]
                    {
                        new
                        {
                            zone = "front",
                            squad = new
                            {
                                leaderDefinitionId = "UNKNOWN",
                                memberDefinitionIds = new[] { "D1", "D2", "D3", "D4" },
                                isFleet = false
                            },
                            holds = 0,
                            defeated = false
                        }
                    },
                    offenseBattles = Array.Empty<object>()
                }
            }
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/gac/opponents/123456789/history",
            request,
            cancellationToken);
        using JsonDocument problem = await ReadJsonAsync(response, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "Unknown Game Data unit",
            problem.RootElement.GetProperty("errors").GetProperty("gac")[0].GetString(),
            StringComparison.Ordinal);
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
        string[] ids =
        [
            "DEFLEADER", "D1", "D2", "D3", "D4",
            "ENEMYLEADER", "E1", "E2", "E3", "E4",
            "COUNTERLEADER", "C1", "C2", "C3", "C4"
        ];
        Dictionary<string, GameUnitDefinition> units = ids.ToDictionary(
            id => id,
            id => new GameUnitDefinition(
                id,
                false,
                null,
                id == "DEFLEADER" ? "Líder defensa" : id,
                null,
                [],
                []),
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
