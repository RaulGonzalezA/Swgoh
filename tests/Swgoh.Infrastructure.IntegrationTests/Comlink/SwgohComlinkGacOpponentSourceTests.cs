using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Comlink;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Comlink;

public sealed class SwgohComlinkGacOpponentSourceTests
{
    [Fact]
    public async Task GetAsync_WithLiveSeasonSchemaAndRateLimit_FindsOpponentFromNestedLeaderboard()
    {
        const string eventId = "CHAMPIONSHIPS_GRAND_ARENA_GA2_EVENT_SEASON_83";
        const string seasonEventInstance = "GA2_SEASON_83A:O1788901200000";
        const string eventsEventInstance = eventId + ":O1788987600000";
        var handler = new GacHandler(eventId, seasonEventInstance, eventsEventInstance);
        var factory = new SingleClientFactory(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://comlink/")
        });
        var source = new SwgohComlinkGacOpponentSource(factory);

        CurrentGacOpponentLookup lookup = await source.GetAsync(
            123456789,
            formatOverride: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.Found, lookup.Status);
        CurrentGacOpponent opponent = Assert.IsType<CurrentGacOpponent>(lookup.Opponent);
        Assert.Equal(987654321, opponent.OpponentAllyCode);
        Assert.Equal("Rival actual", opponent.OpponentName);
        Assert.Equal("opponent-id", opponent.OpponentPlayerId);
        Assert.Equal(GacLeague.Kyber, opponent.League);
        Assert.Equal(GacFormat.ThreeVsThree, opponent.Format);
        Assert.Equal("SeasonStatus", opponent.FormatSource);
        Assert.Equal("BracketOrderPairing", opponent.OpponentResolutionMethod);
        Assert.Equal(eventId, opponent.EventId);
        Assert.Equal(seasonEventInstance, opponent.EventInstanceId);
        Assert.EndsWith(":KYBER:9", opponent.BracketId, StringComparison.Ordinal);
        Assert.Equal(1, handler.PlayerRequests);
        Assert.Equal(1, handler.PlayerArenaRequests);
        Assert.Equal(1, handler.RateLimitResponses);
        Assert.True(handler.MissingBracketResponses > 0);
        Assert.InRange(handler.BracketRequests, 5, 12);
        Assert.All(handler.GroupIds, groupId =>
            Assert.StartsWith(seasonEventInstance, groupId, StringComparison.Ordinal));
        Assert.DoesNotContain(handler.GroupIds, groupId =>
            groupId.StartsWith(eventsEventInstance, StringComparison.Ordinal));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class GacHandler(
        string eventId,
        string seasonEventInstance,
        string eventsEventInstance) : HttpMessageHandler
    {
        private readonly ConcurrentBag<string> groupIds = [];
        private int bracketRequests;
        private int missingBracketResponses;
        private int playerArenaRequests;
        private int playerRequests;
        private int rateLimitResponses;
        private int targetBracketRequests;

        public int BracketRequests => bracketRequests;

        public int MissingBracketResponses => missingBracketResponses;

        public int PlayerArenaRequests => playerArenaRequests;

        public int PlayerRequests => playerRequests;

        public int RateLimitResponses => rateLimitResponses;

        public IReadOnlyCollection<string> GroupIds => groupIds.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath.Trim('/') ?? string.Empty;
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return path switch
            {
                "player" => Player(),
                "playerArena" => PlayerArena(body),
                "getEvents" => Json($$"""
                    {
                      "gameEvent": [{
                        "id": "{{eventId}}",
                        "type": 10,
                        "instance": [{
                          "id": "O1788987600000",
                          "startTime": "1788987600000",
                          "endTime": "1792000000000"
                        }]
                      }]
                    }
                    """),
                "getLeaderboard" => Leaderboard(body),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private HttpResponseMessage Player()
        {
            Interlocked.Increment(ref playerRequests);
            return Json($$"""
                {
                  "allyCode": "123456789",
                  "playerId": "self-id",
                  "name": "Yo",
                  "playerRating": {
                    "playerSkillRating": { "skillRating": 3200 },
                    "playerRankStatus": { "leagueId": "KYBER", "divisionId": 10 }
                  },
                  "seasonStatus": [{
                    "seasonId": "4zone_3v3_ga2_c3s1_83a",
                    "eventInstanceId": "{{seasonEventInstance}}",
                    "league": "KYBER",
                    "division": 10,
                    "rank": 81,
                    "endTime": "1791234000000"
                  }]
                }
                """);
        }

        private HttpResponseMessage PlayerArena(string body)
        {
            Interlocked.Increment(ref playerArenaRequests);
            using JsonDocument request = JsonDocument.Parse(body);
            JsonElement payload = request.RootElement.GetProperty("payload");
            if (payload.TryGetProperty("playerId", out JsonElement playerId) &&
                string.Equals(playerId.GetString(), "opponent-id", StringComparison.Ordinal))
            {
                return Json("""
                    {
                      "allyCode": "987654321",
                      "playerId": "opponent-id",
                      "name": "Rival actual"
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        private HttpResponseMessage Leaderboard(string body)
        {
            Interlocked.Increment(ref bracketRequests);
            using JsonDocument request = JsonDocument.Parse(body);
            string groupId = request.RootElement.GetProperty("payload").GetProperty("groupId").GetString()
                ?? string.Empty;
            groupIds.Add(groupId);

            if (string.Equals(groupId, seasonEventInstance + ":KYBER:9", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref targetBracketRequests) == 1)
                {
                    Interlocked.Increment(ref rateLimitResponses);
                    return BadRequest("""{"code":6,"message":"Rate exceeded!"}""");
                }

                return Json(NestedBracket([
                    ("a", "A"),
                    ("b", "B"),
                    ("self-id", "Yo"),
                    ("opponent-id", "Rival en bracket"),
                    ("c", "C"),
                    ("d", "D"),
                    ("e", "E"),
                    ("f", "F")
                ]));
            }

            Interlocked.Increment(ref missingBracketResponses);
            return BadRequest("""{"code":5,"message":"Leaderboard group not found"}""");
        }

        private static string NestedBracket(IReadOnlyCollection<(string Id, string Name)> players)
        {
            string entries = string.Join(",", players.Select(player =>
                $$"""{"id":"{{player.Id}}","name":"{{player.Name}}","level":85,"power":10000000}"""));
            return $$"""
                {
                  "player": [],
                  "leaderboard": [{
                    "player": [{{entries}}],
                    "id": "",
                    "playerStatus": { "rank": 1, "rankDelta": 0, "score": 0, "scoreDelta": 0, "tier": 0 }
                  }],
                  "playerStatus": null
                }
                """;
        }

        private static HttpResponseMessage BadRequest(string json) => new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
