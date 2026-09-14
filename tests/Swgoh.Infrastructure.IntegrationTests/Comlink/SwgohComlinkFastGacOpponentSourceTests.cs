using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Comlink;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Comlink;

public sealed class SwgohComlinkFastGacOpponentSourceTests
{
    [Fact]
    public async Task GetAsync_LocatesBracketBySkillRatingAndReusesExactLocation()
    {
        var handler = new RatingOrderedGacHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://comlink/") };
        var source = new SwgohComlinkFastGacOpponentSource(
            new SingleClientFactory(client),
            NullLogger<SwgohComlinkFastGacOpponentSource>.Instance);

        CurrentGacOpponentLookup result = await source.GetAsync(
            RatingOrderedGacHandler.SelfAllyCode,
            formatOverride: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.Found, result.Status);
        CurrentGacOpponent opponent = Assert.IsType<CurrentGacOpponent>(result.Opponent);
        Assert.Equal(RatingOrderedGacHandler.OpponentAllyCode, opponent.OpponentAllyCode);
        Assert.Equal("Rival actual", opponent.OpponentName);
        Assert.Equal(GacLeague.Kyber, opponent.League);
        Assert.Equal(GacFormat.ThreeVsThree, opponent.Format);
        Assert.Equal("PvpScoreRankPairing", opponent.OpponentResolutionMethod);
        Assert.Equal(RatingOrderedGacHandler.ActiveEventInstance, opponent.EventInstanceId);
        Assert.EndsWith(":KYBER:70", opponent.BracketId, StringComparison.Ordinal);
        Assert.Equal(3, opponent.RoundNumber);
        Assert.InRange(handler.LeaderboardRequests, 1, 24);
        Assert.All(handler.GroupIds, groupId => Assert.StartsWith(RatingOrderedGacHandler.ActiveEventInstance, groupId, StringComparison.Ordinal));

        int requestsAfterDiscovery = handler.LeaderboardRequests;
        CurrentGacOpponentLookup locationCached = await source.GetAsync(
            RatingOrderedGacHandler.SelfAllyCode,
            GacFormat.ThreeVsThree,
            TestContext.Current.CancellationToken);

        Assert.Equal(CurrentGacOpponentStatus.Found, locationCached.Status);
        Assert.Equal(requestsAfterDiscovery + 1, handler.LeaderboardRequests);
        Assert.EndsWith(":KYBER:70", handler.GroupIds.Last(), StringComparison.Ordinal);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RatingOrderedGacHandler : HttpMessageHandler
    {
        public const long SelfAllyCode = 123456789;
        public const long OpponentAllyCode = 987654321;
        public const string ActiveEventInstance = "CHAMPIONSHIPS_GRAND_ARENA_GA2_EVENT_SEASON_83:O2000000000000";
        private const string EventId = "CHAMPIONSHIPS_GRAND_ARENA_GA2_EVENT_SEASON_83";
        private const string SelfPlayerId = "self-id";
        private const int TargetBracket = 70;
        private const int LastBracket = 127;

        private readonly List<string> groupIds = [];

        public int LeaderboardRequests { get; private set; }

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
                "getEvents" => Events(),
                "getLeaderboard" => Leaderboard(body),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static HttpResponseMessage PlayerArena(string body)
        {
            using JsonDocument request = JsonDocument.Parse(body);
            JsonElement payload = request.RootElement.GetProperty("payload");
            if (payload.TryGetProperty("allyCode", out _))
            {
                return Json($$"""
                    {
                      "allyCode": "{{SelfAllyCode}}",
                      "playerId": "{{SelfPlayerId}}",
                      "name": "Yo",
                      "playerRating": {
                        "playerSkillRating": { "skillRating": 3300 },
                        "playerRankStatus": { "leagueId": "KYBER", "divisionId": 10 }
                      }
                    }
                    """);
            }

            string playerId = payload.GetProperty("playerId").GetString() ?? string.Empty;
            if (string.Equals(playerId, "p-70-0", StringComparison.Ordinal))
            {
                return Profile(playerId, OpponentAllyCode, "Rival actual", 3300);
            }

            if (!TryParseSyntheticPlayerId(playerId, out int bracket, out int slot))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            long allyCode = 700_000_000L + (bracket * 10L) + slot;
            int skillRating = 4000 - (bracket * 10);
            return Profile(playerId, allyCode, $"Jugador {bracket}-{slot}", skillRating);
        }

        private static HttpResponseMessage Events() => Json($$"""
            {
              "gameEvent": [{
                "id": "{{EventId}}",
                "type": 10,
                "instance": [
                  {
                    "id": "O1000000000000",
                    "startTime": "1",
                    "endTime": "2"
                  },
                  {
                    "id": "O2000000000000",
                    "startTime": "1",
                    "endTime": "9999999999999"
                  }
                ]
              }]
            }
            """);

        private HttpResponseMessage Leaderboard(string body)
        {
            LeaderboardRequests++;
            using JsonDocument request = JsonDocument.Parse(body);
            string groupId = request.RootElement.GetProperty("payload").GetProperty("groupId").GetString()
                ?? string.Empty;
            groupIds.Add(groupId);

            if (!groupId.StartsWith(ActiveEventInstance + ":KYBER:", StringComparison.Ordinal) ||
                !int.TryParse(groupId[(groupId.LastIndexOf(':') + 1)..], out int bracket) ||
                bracket is < 0 or > LastBracket)
            {
                return Json("""{"player":[],"leaderboard":[]}""");
            }

            string[] players = Enumerable.Range(0, 8)
                .Select(slot => Participant(bracket, slot))
                .ToArray();
            return Json($$"""
                {
                  "player": [],
                  "leaderboard": [{ "player": [{{string.Join(",", players)}}] }]
                }
                """);
        }

        private static string Participant(int bracket, int slot)
        {
            string playerId = bracket == TargetBracket && slot == 1
                ? SelfPlayerId
                : $"p-{bracket}-{slot}";
            string name = bracket == TargetBracket && slot == 1 ? "Yo" : $"Jugador {bracket}-{slot}";
            int score = bracket == TargetBracket
                ? slot switch
                {
                    0 or 1 => 2,
                    2 or 3 or 4 or 5 => 1,
                    _ => 0
                }
                : 0;
            return $"{{\"id\":\"{playerId}\",\"name\":\"{name}\",\"level\":85,\"power\":12000000,\"pvpStatus\":{{\"rank\":{slot + 1},\"rankDelta\":0,\"score\":{score},\"scoreDelta\":0,\"tier\":0}}}}";
        }

        private static HttpResponseMessage Profile(string playerId, long allyCode, string name, int skillRating) => Json($$"""
            {
              "allyCode": "{{allyCode}}",
              "playerId": "{{playerId}}",
              "name": "{{name}}",
              "playerRating": {
                "playerSkillRating": { "skillRating": {{skillRating}} },
                "playerRankStatus": { "leagueId": "KYBER", "divisionId": 10 }
              }
            }
            """);

        private static bool TryParseSyntheticPlayerId(string value, out int bracket, out int slot)
        {
            bracket = default;
            slot = default;
            string[] parts = value.Split('-');
            return parts.Length == 3 &&
                   string.Equals(parts[0], "p", StringComparison.Ordinal) &&
                   int.TryParse(parts[1], out bracket) &&
                   int.TryParse(parts[2], out slot);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
