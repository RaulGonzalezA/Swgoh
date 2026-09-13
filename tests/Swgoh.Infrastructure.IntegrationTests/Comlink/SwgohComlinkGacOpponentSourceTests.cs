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
    public async Task GetAsync_DetectsLeagueFormatBracketAndDirectOpponent()
    {
        const string eventId = "CHAMPIONSHIPS_GRAND_ARENA_GA2_EVENT_SEASON_83";
        const string eventInstance = eventId + ":O1788998400000";
        var handler = new GacHandler(eventId, eventInstance);
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
        Assert.Equal("DirectBracketMetadata", opponent.OpponentResolutionMethod);
        Assert.EndsWith(":KYBER:1", opponent.BracketId, StringComparison.Ordinal);
        Assert.Equal(2, handler.BracketRequests);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class GacHandler(string eventId, string eventInstance) : HttpMessageHandler
    {
        public int BracketRequests { get; private set; }

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
                "playerArena" => Json("""
                    {
                      "allyCode": "123456789",
                      "playerId": "self-id",
                      "name": "Yo",
                      "playerRating": { "league": "Kyber", "division": 15, "skillRating": 3200 },
                      "seasonStatus": [{
                        "seasonId": "4zone_3v3_ga2_c3s1_83a",
                        "eventInstanceId": "CHAMPIONSHIPS_GRAND_ARENA_GA2_EVENT_SEASON_83:O1788998400000",
                        "league": "Kyber",
                        "rank": 9
                      }]
                    }
                    """),
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

        private HttpResponseMessage Leaderboard(string body)
        {
            BracketRequests++;
            using JsonDocument request = JsonDocument.Parse(body);
            string? groupId = request.RootElement.GetProperty("payload").GetProperty("groupId").GetString();
            if (!string.Equals(groupId, eventInstance + ":KYBER:1", StringComparison.Ordinal))
            {
                return Json("{\"player\":[]}");
            }

            return Json("""
                {
                  "player": [
                    { "allyCode": "111111111", "playerId": "p1", "name": "P1" },
                    { "allyCode": "222222222", "playerId": "p2", "name": "P2" },
                    { "allyCode": "123456789", "playerId": "self-id", "name": "Yo", "opponentPlayerId": "opponent-id" },
                    { "allyCode": "987654321", "playerId": "opponent-id", "name": "Rival actual" },
                    { "allyCode": "333333333", "playerId": "p3", "name": "P3" },
                    { "allyCode": "444444444", "playerId": "p4", "name": "P4" },
                    { "allyCode": "555555555", "playerId": "p5", "name": "P5" },
                    { "allyCode": "666666666", "playerId": "p6", "name": "P6" }
                  ]
                }
                """);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
