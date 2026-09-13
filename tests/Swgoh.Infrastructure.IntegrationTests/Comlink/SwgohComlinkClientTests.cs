using System.Net;
using System.Text;
using System.Text.Json;

using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;
using Swgoh.Infrastructure.Comlink;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Comlink;

public sealed class SwgohComlinkClientTests
{
    [Fact]
    public async Task GetPlayerAsync_ConvertsComlinkSkillTierBeforeCountingZetasAndOmicrons()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string playerJson = """
            {
              "allyCode":"476825771",
              "playerId":"player-id",
              "name":"Aberronko",
              "guildId":"guild-id",
              "guildName":"Guild",
              "level":85,
              "rosterUnit":[
                {
                  "id":"unit-1",
                  "definitionId":"CHARACTER:SEVEN_STAR",
                  "currentLevel":85,
                  "currentRarity":7,
                  "currentTier":13,
                  "relic":{"currentTier":11},
                  "equippedStatMod":[{},{}],
                  "skill":[
                    {"id":"zeta-and-omicron","tier":6},
                    {"id":"zeta-before-omicron","tier":5},
                    {"id":"not-yet-zeta","tier":5}
                  ]
                }
              ]
            }
            """;

        using var httpClient = CreateHttpClient(playerJson);
        var stats = new FakeStatsClient(new Dictionary<string, long> { ["unit-1"] = 42_000 });
        var catalog = new FakeGameDataCatalog(new GameDataCatalog(
            new Dictionary<string, GameUnitDefinition>
            {
                ["CHARACTER"] = new("CHARACTER", false, null, "CHARACTER", null, [], [])
            },
            new Dictionary<string, GameSkillDefinition>
            {
                ["zeta-and-omicron"] = new("zeta-and-omicron", 7, 8),
                ["zeta-before-omicron"] = new("zeta-before-omicron", 7, 8),
                ["not-yet-zeta"] = new("not-yet-zeta", 8, null)
            },
            []));
        var client = new SwgohComlinkClient(httpClient, stats, catalog);

        ImportedPlayer result = await client.GetPlayerAsync(476_825_771, cancellationToken);

        Assert.Equal(42_000, result.GalacticPower);
        ImportedRosterUnit unit = Assert.Single(result.Roster);
        Assert.Equal("CHARACTER", unit.DefinitionId);
        Assert.Equal(9, unit.RelicTier);
        Assert.Equal(2, unit.EquippedModCount);
        Assert.Equal(2, unit.ZetaCount);
        Assert.Equal(1, unit.OmicronCount);
    }

    [Fact]
    public async Task GetPlayerAsync_WithTacticalData_MapsStatsModsAndDatacrons()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string playerJson = """
            {
              "allyCode":"476825771",
              "playerId":"player-id",
              "name":"Aberronko",
              "level":85,
              "rosterUnit":[
                {
                  "id":"unit-1",
                  "definitionId":"CHARACTER:SEVEN_STAR",
                  "currentLevel":85,
                  "currentRarity":7,
                  "currentTier":13,
                  "relic":{"currentTier":11},
                  "equippedStatMod":[
                    {
                      "definitionId":"461",
                      "primaryStat":{"unitStatId":5,"unscaledDecimalValue":"3200000000"},
                      "secondaryStat":[]
                    },
                    {
                      "definitionId":"521",
                      "primaryStat":{"unitStatId":1,"unscaledDecimalValue":"500000000000"},
                      "secondaryStat":[{"unitStatId":5,"unscaledDecimalValue":"2500000000"}]
                    }
                  ],
                  "skill":[]
                }
              ],
              "datacron":[
                {
                  "id":"dc-1",
                  "setId":"set-14",
                  "templateId":"template-1",
                  "tier":9,
                  "locked":true,
                  "affix":[
                    {"abilityId":"ability-1","requiredRelicTier":7,"tag":["JEDI"]},
                    {"statType":5,"statValue":"1200000000"}
                  ]
                }
              ]
            }
            """;

        using var httpClient = CreateHttpClient(playerJson);
        var stats = new FakeStatsClient(
            new Dictionary<string, long> { ["unit-1"] = 50_000 },
            new Dictionary<string, RosterUnitStats>
            {
                ["unit-1"] = new(Health: 120_000m, Protection: 95_000m, Speed: 333m, PhysicalDamage: 11_000m)
            });
        var client = new SwgohComlinkClient(httpClient, stats, CatalogWithCharacter("CHARACTER"));

        ImportedPlayer result = await client.GetPlayerAsync(476_825_771, cancellationToken);

        ImportedRosterUnit unit = Assert.Single(result.Roster);
        Assert.Equal(333m, unit.Stats?.Speed);
        Assert.NotNull(unit.Mods);
        Assert.Equal(2, unit.Mods.EquippedCount);
        Assert.Equal(1, unit.Mods.SixDotCount);
        Assert.Equal(1, unit.Mods.SpeedSetModCount);
        Assert.Equal(1, unit.Mods.SpeedPrimaryCount);
        Assert.Equal(57m, unit.Mods.SpeedBonus);

        PlayerDatacron datacron = Assert.Single(result.Datacrons!);
        Assert.Equal("dc-1", datacron.Id);
        Assert.Equal(9, datacron.Tier);
        Assert.True(datacron.Locked);
        Assert.True(datacron.HasAbilityAffix);
        Assert.Equal(7, datacron.HighestRequiredRelicTier);
        Assert.Equal(2, datacron.Affixes.Count);
    }

    [Fact]
    public async Task GetPlayerAsync_WhenComlinkReturnsDifferentAllyCode_RejectsPayload()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string playerJson = """
            {
              "allyCode":"111222333",
              "playerId":"player-id",
              "name":"Wrong player",
              "level":85,
              "rosterUnit":[]
            }
            """;
        using var httpClient = CreateHttpClient(playerJson);
        var client = new SwgohComlinkClient(
            httpClient,
            new FakeStatsClient(new Dictionary<string, long>()),
            EmptyCatalog());

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetPlayerAsync(476_825_771, cancellationToken));

        Assert.Contains("returned ally code", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPlayerAsync_WhenStatsOmitsRosterUnitPower_RejectsPayload()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string playerJson = """
            {
              "allyCode":"476825771",
              "playerId":"player-id",
              "name":"Aberronko",
              "level":85,
              "rosterUnit":[
                {
                  "id":"unit-1",
                  "definitionId":"CHARACTER:SEVEN_STAR",
                  "currentLevel":85,
                  "currentRarity":7,
                  "currentTier":13,
                  "skill":[]
                }
              ]
            }
            """;
        using var httpClient = CreateHttpClient(playerJson);
        var client = new SwgohComlinkClient(
            httpClient,
            new FakeStatsClient(new Dictionary<string, long>()),
            CatalogWithCharacter("CHARACTER"));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetPlayerAsync(476_825_771, cancellationToken));

        Assert.Contains("did not return calculated data", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPlayerAsync_WhenGameDataCannotClassifyUnit_RejectsPayload()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string playerJson = """
            {
              "allyCode":"476825771",
              "playerId":"player-id",
              "name":"Aberronko",
              "level":85,
              "rosterUnit":[
                {
                  "id":"unit-1",
                  "definitionId":"UNKNOWN:SEVEN_STAR",
                  "currentLevel":85,
                  "currentRarity":7,
                  "currentTier":13,
                  "skill":[]
                }
              ]
            }
            """;
        using var httpClient = CreateHttpClient(playerJson);
        var client = new SwgohComlinkClient(
            httpClient,
            new FakeStatsClient(new Dictionary<string, long> { ["unit-1"] = 42_000 }),
            EmptyCatalog());

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetPlayerAsync(476_825_771, cancellationToken));

        Assert.Contains("Game Data does not contain", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPlayerAsync_WithInvalidAllyCode_ThrowsBeforeCallingProvider()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = CreateHttpClient("{}");
        var client = new SwgohComlinkClient(
            httpClient,
            new FakeStatsClient(new Dictionary<string, long>()),
            EmptyCatalog());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetPlayerAsync(123, cancellationToken));
    }

    private static HttpClient CreateHttpClient(string json) => new(new JsonHandler(json))
    {
        BaseAddress = new Uri("http://comlink.test/")
    };

    private static FakeGameDataCatalog EmptyCatalog() =>
        new(new GameDataCatalog(
            new Dictionary<string, GameUnitDefinition>(),
            new Dictionary<string, GameSkillDefinition>(),
            []));

    private static FakeGameDataCatalog CatalogWithCharacter(string definitionId) =>
        new(new GameDataCatalog(
            new Dictionary<string, GameUnitDefinition>
            {
                [definitionId] = new(definitionId, false, null, definitionId, null, [], [])
            },
            new Dictionary<string, GameSkillDefinition>(),
            []));

    private sealed class FakeStatsClient(
        IReadOnlyDictionary<string, long> powerByUnit,
        IReadOnlyDictionary<string, RosterUnitStats>? statsByUnit = null) : ISwgohStatsClient
    {
        public Task<IReadOnlyDictionary<string, CalculatedRosterUnitStats>> CalculateRosterStatsAsync(
            IReadOnlyCollection<JsonElement> roster,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, CalculatedRosterUnitStats> result = powerByUnit.ToDictionary(
                pair => pair.Key,
                pair => new CalculatedRosterUnitStats(
                    pair.Value,
                    statsByUnit?.GetValueOrDefault(pair.Key)),
                StringComparer.Ordinal);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeGameDataCatalog(GameDataCatalog catalog) : ISwgohGameDataCatalog
    {
        public Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(catalog);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
