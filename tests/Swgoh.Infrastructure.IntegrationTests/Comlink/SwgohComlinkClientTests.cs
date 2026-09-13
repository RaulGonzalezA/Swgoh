using System.Net;
using System.Text;
using System.Text.Json;

using Swgoh.Application.GameData;
using Swgoh.Application.Players;
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

        using var httpClient = new HttpClient(new JsonHandler(playerJson))
        {
            BaseAddress = new Uri("http://comlink.test/")
        };
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
    public async Task GetPlayerAsync_WithInvalidAllyCode_ThrowsBeforeCallingProvider()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new JsonHandler("{}"))
        {
            BaseAddress = new Uri("http://comlink.test/")
        };
        var client = new SwgohComlinkClient(
            httpClient,
            new FakeStatsClient(new Dictionary<string, long>()),
            new FakeGameDataCatalog(new GameDataCatalog(
                new Dictionary<string, GameUnitDefinition>(),
                new Dictionary<string, GameSkillDefinition>(),
                [])));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetPlayerAsync(123, cancellationToken));
    }

    private sealed class FakeStatsClient(IReadOnlyDictionary<string, long> powerByUnit) : ISwgohStatsClient
    {
        public Task<IReadOnlyDictionary<string, long>> CalculateGalacticPowerAsync(
            IReadOnlyCollection<JsonElement> roster,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(powerByUnit);
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
