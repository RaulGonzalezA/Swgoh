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
    public async Task GetAsync_WithSparseBadRequestBrackets_UsesSeasonInstanceAndFindsOpponent()
    {
        const string eventId = "CHAMPIONSHIPS_GRAND_ARENA_GA2_EVENT_SEASON_83";
        const string currentEventInstance = eventId + ":O1788998400000";
        var handler = new GacHandler(eventId, currentEventInstance);
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
        Assert.Equal("SeasonAlternationFallback", opponent.FormatSource);
        Assert.Equal("BracketOrderPairing", opponent.OpponentResolutionMethod);
        Assert.Equal(currentEventInstance, opponent.EventInstanceId);
        Assert.EndsWith(":KYBER:9", opponent.BracketId, StringComparison.Ordinal);
        Assert.Equal(2, handler.PlayerArenaRequests);
        Assert.InRange(handler.BracketRequests, 10, 40);
        Assert.True(handler.BadRequestBracketResponses > 0);
        Assert.DoesNotContain(handler.GroupIds, groupId => groupId.EndsWith(":KYBER:0", StringComparison.Ordinal) && handler.GroupIds.Count == 1);
        Assert.All(handler.GroupIds, groupId => Assert.StartsWith(currentEventInstance, groupId, StringComparison.Ordinal));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class GacHandler(string eventId, string currentEventInstance) : HttpMessageHandler
    {
        private readonly List<string> groupIds = [];

        public int BracketRequests { get; private set; }

        public int PlayerArenaRequests { get; private set; }

        public int BadRequestBracketResponses { get; private set; }

        public IReadOnlyCollection<string> GroupIds => groupIds;

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
                        "instance": [
                          { "id": "O1788000000000" },
                          { "id": "O1788998400000" }
                        ]
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

            return Json($$"""
                {
                  "allyCode": "123456789",
                  "playerId": "self-id",
                  "name": "Yo",
                  "playerRating": { "league": "Kyber", "division": 15, "skillRating": 3200 },
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
            BracketRequests++;
            using JsonDocument request = JsonDocument.Parse(body);
            string groupId = request.RootElement.GetProperty("payload").GetProperty("groupId").GetString()
                ?? string.Empty;
            groupIds.Add(groupId);

            if (string.Equals(groupId, currentEventInstance + ":KYBER:9", StringComparison.Ordinal))
            {
                return Json(Bracket([
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

            BadRequestBracketResponses++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        private static string Bracket(IReadOnlyCollection<(string Id, string Name)> players)
        {
            string entries = string.Join(",", players.Select(player =>
                $$"""{"id":"{{player.Id}}","name":"{{player.Name}}","level":85,"power":10000000}"""));
            return $$"""{"player":[{{entries}}]}""";
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
