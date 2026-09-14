using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Comlink;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Comlink;

public sealed class SwgohComlinkCurrentRoundOpponentSourceTests
{
    [Fact]
    public async Task GetAsync_WithRoundTwoSwissStandings_ReplacesAdjacentPlayerWithActualMatchup()
    {
        const string eventId = "CHAMPIONSHIPS_GRAND_ARENA_GA2_EVENT_SEASON_83";
        const string currentEventInstance = eventId + ":O1788998400000";
        var handler = new GacHandler(eventId, currentEventInstance);
        var factory = new SingleClientFactory(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://comlink/")
        });
        var bracketSource = new SwgohComlinkGacOpponentSource(factory, NullLogger<SwgohComlinkGacOpponentSource>.Instance);
        var source = new SwgohComlinkCurrentRoundOpponentSource(bracketSource, factory);

        CurrentGacOpponentLookup lookup = await source.GetAsync(
            123456789,
            formatOverride: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.Found, lookup.Status);
        CurrentGacOpponent opponent = Assert.IsType<CurrentGacOpponent>(lookup.Opponent);
        Assert.Equal(987654321, opponent.OpponentAllyCode);
        Assert.Equal("Rival actual", opponent.OpponentName);
        Assert.Equal("opponent-id", opponent.OpponentPlayerId);
        Assert.Equal("PvpScoreRankPairing", opponent.OpponentResolutionMethod);
        Assert.Equal(GacLeague.Kyber, opponent.League);
        Assert.Equal(GacFormat.ThreeVsThree, opponent.Format);
        Assert.EndsWith(":KYBER:9", opponent.BracketId, StringComparison.Ordinal);
        Assert.Equal(4, handler.PlayerArenaRequests);
        Assert.True(handler.ExactBracketRequests >= 2);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class GacHandler(string eventId, string currentEventInstance) : HttpMessageHandler
    {
        public int PlayerArenaRequests { get; private set; }

        public int ExactBracketRequests { get; private set; }

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
                "playerArena" => PlayerArena(body),
                "getEvents" => Json($$"""
                    {
                      "gameEvent": [{
                        "id": "{{eventId}}",
                        "type": 10,
                        "instance": [{ "id": "O1788998400000" }]
                      }]
                    }
                    """),
                "getLeaderboard" => Leaderboard(body),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private HttpResponseMessage PlayerArena(string body)
        {
            PlayerArenaRequests++;
            using JsonDocument request = JsonDocument.Parse(body);
            JsonElement payload = request.RootElement.GetProperty("payload");
            if (payload.TryGetProperty("playerId", out JsonElement requestedPlayerId))
            {
                return requestedPlayerId.GetString() switch
                {
                    "wrong-id" => Json("""
                        {
                          "allyCode": "222333444",
                          "playerId": "wrong-id",
                          "name": "Rival adyacente incorrecto"
                        }
                        """),
                    "opponent-id" => Json("""
                        {
                          "allyCode": "987654321",
                          "playerId": "opponent-id",
                          "name": "Rival actual"
                        }
                        """),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
            }

            return Json($$"""
                {
                  "allyCode": "123456789",
                  "playerId": "self-id",
                  "name": "Yo",
                  "playerRating": { "league": "Kyber", "division": 10, "skillRating": 3073 },
                  "seasonStatus": [{
                    "seasonId": "{{eventId}}",
                    "eventInstanceId": "{{currentEventInstance}}",
                    "league": "Kyber",
                    "rank": 81
                  }]
                }
                """);
        }

        private HttpResponseMessage Leaderboard(string body)
        {
            using JsonDocument request = JsonDocument.Parse(body);
            string groupId = request.RootElement.GetProperty("payload").GetProperty("groupId").GetString()
                ?? string.Empty;
            if (!string.Equals(groupId, currentEventInstance + ":KYBER:9", StringComparison.Ordinal))
            {
                return Json("""{"code":5,"message":"Leaderboard not found"}""", HttpStatusCode.BadRequest);
            }

            ExactBracketRequests++;
            return Json("""
                {
                  "player": [],
                  "leaderboard": [{
                    "player": [
                      {
                        "id": "wrong-id",
                        "name": "Rival adyacente incorrecto",
                        "pvpStatus": { "rank": 1, "rankDelta": 0, "score": 1, "scoreDelta": 0, "tier": 0 }
                      },
                      {
                        "id": "self-id",
                        "name": "Yo",
                        "pvpStatus": { "rank": 2, "rankDelta": 0, "score": 1, "scoreDelta": 0, "tier": 0 }
                      },
                      {
                        "id": "opponent-id",
                        "name": "Rival actual",
                        "pvpStatus": { "rank": 3, "rankDelta": 0, "score": 1, "scoreDelta": 0, "tier": 0 }
                      },
                      {
                        "id": "winner-four",
                        "name": "Ganador cuatro",
                        "pvpStatus": { "rank": 4, "rankDelta": 0, "score": 1, "scoreDelta": 0, "tier": 0 }
                      },
                      {
                        "id": "loser-five",
                        "name": "Perdedor cinco",
                        "pvpStatus": { "rank": 5, "rankDelta": 0, "score": 0, "scoreDelta": 0, "tier": 0 }
                      },
                      {
                        "id": "loser-six",
                        "name": "Perdedor seis",
                        "pvpStatus": { "rank": 6, "rankDelta": 0, "score": 0, "scoreDelta": 0, "tier": 0 }
                      },
                      {
                        "id": "loser-seven",
                        "name": "Perdedor siete",
                        "pvpStatus": { "rank": 7, "rankDelta": 0, "score": 0, "scoreDelta": 0, "tier": 0 }
                      },
                      {
                        "id": "loser-eight",
                        "name": "Perdedor ocho",
                        "pvpStatus": { "rank": 8, "rankDelta": 0, "score": 0, "scoreDelta": 0, "tier": 0 }
                      }
                    ],
                    "id": "",
                    "playerStatus": { "rank": 1, "rankDelta": 0, "score": 0, "scoreDelta": 0, "tier": 0 }
                  }],
                  "playerStatus": null
                }
                """);
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
