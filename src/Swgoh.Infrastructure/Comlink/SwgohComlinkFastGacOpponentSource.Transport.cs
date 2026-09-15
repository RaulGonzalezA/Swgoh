using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed partial class SwgohComlinkFastGacOpponentSource
{
    private async Task<BracketData?> ReadBracketAsync(
        int index,
        string leagueToken,
        string eventInstanceId,
        LookupDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        string bracketId = $"{eventInstanceId}:{leagueToken}:{index}";
        object request = new
        {
            payload = new
            {
                leaderboardType = 4,
                eventInstanceId,
                groupId = bracketId
            },
            enums = false
        };

        HttpClient client = httpClientFactory.CreateClient(SwgohComlinkGacOpponentSource.HttpClientName);
        for (int attempt = 0; attempt <= RateLimitRetryCount; attempt++)
        {
            diagnostics.LeaderboardRequests++;
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "getLeaderboard",
                request,
                cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                if (!body.Contains("Rate exceeded", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                diagnostics.RateLimits++;
                if (attempt == RateLimitRetryCount)
                {
                    throw new HttpRequestException(
                        $"Comlink rate limit persisted while reading GAC bracket '{bracketId}'.",
                        inner: null,
                        response.StatusCode);
                }

                await Task.Delay(GetRateLimitDelay(attempt, index), cancellationToken).ConfigureAwait(false);
                continue;
            }

            response.EnsureSuccessStatusCode();
            using JsonDocument document = JsonDocument.Parse(body);
            if (!TryGetLeaderboardPlayers(document.RootElement, out JsonElement playersElement))
            {
                return null;
            }

            Participant[] players =
            [
                .. playersElement.EnumerateArray()
                    .Select(ReadParticipant)
                    .Where(participant => participant is not null)
                    .Select(participant => participant!)
            ];
            return players.Length == 0 ? null : new BracketData(index, bracketId, players);
        }

        return null;
    }

    private async Task<JsonDocument> GetArenaProfileAsync(
        object payload,
        LookupDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        diagnostics.ArenaRequests++;
        return await PostAsync(
            "playerArena",
            new { payload, enums = false },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ParticipantProfile?> ResolveProfileAsync(
        Participant participant,
        LookupDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        if (participant.AllyCode is long allyCode && allyCode is >= 100_000_000 and <= 999_999_999)
        {
            return new ParticipantProfile(allyCode, participant.Name, participant.PlayerId);
        }

        if (string.IsNullOrWhiteSpace(participant.PlayerId))
        {
            return null;
        }

        using JsonDocument profile = await GetArenaProfileAsync(
            new { playerId = participant.PlayerId, playerDetailsOnly = true },
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        if (!TryReadLongProperty(profile.RootElement, "allyCode", out long resolvedAllyCode) ||
            resolvedAllyCode is < 100_000_000 or > 999_999_999)
        {
            return null;
        }

        return new ParticipantProfile(
            resolvedAllyCode,
            ReadString(profile.RootElement, "name") ?? participant.Name,
            ReadString(profile.RootElement, "playerId") ?? ReadString(profile.RootElement, "id") ?? participant.PlayerId);
    }

    private async Task<JsonDocument> PostAsync(string path, object request, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(SwgohComlinkGacOpponentSource.HttpClientName);
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static Participant? ResolveCurrentOpponent(
        IReadOnlyList<Participant> players,
        int playerIndex,
        string playerId,
        long allyCode,
        out string resolutionMethod)
    {
        Participant current = players[playerIndex];
        if (TryReadPvpInt(current.Element, "score", out int currentScore))
        {
            Participant[] sameRecord =
            [
                .. players.Where(participant =>
                    TryReadPvpInt(participant.Element, "score", out int score) && score == currentScore)
            ];
            if (sameRecord.Length is 2 or 4)
            {
                var ranked = new List<(Participant Participant, int Rank)>(sameRecord.Length);
                foreach (Participant participant in sameRecord)
                {
                    if (!TryReadPvpInt(participant.Element, "rank", out int rank))
                    {
                        ranked.Clear();
                        break;
                    }

                    ranked.Add((participant, rank));
                }

                if (ranked.Count == sameRecord.Length && ranked.Select(value => value.Rank).Distinct().Count() == ranked.Count)
                {
                    var ordered = ranked.OrderBy(value => value.Rank).ToArray();
                    int currentIndex = Array.FindIndex(ordered, value =>
                        string.Equals(value.Participant.PlayerId, playerId, StringComparison.Ordinal) ||
                        value.Participant.AllyCode == allyCode);
                    if (currentIndex >= 0)
                    {
                        int pairedIndex = ordered.Length - 1 - currentIndex;
                        if (pairedIndex >= 0 && pairedIndex < ordered.Length && pairedIndex != currentIndex)
                        {
                            resolutionMethod = "PvpScoreRankPairing";
                            return ordered[pairedIndex].Participant;
                        }
                    }
                }
            }
        }

        if (players.Count == BracketSize)
        {
            int pairedIndex = playerIndex % 2 == 0 ? playerIndex + 1 : playerIndex - 1;
            if (pairedIndex >= 0 && pairedIndex < players.Count)
            {
                resolutionMethod = "BracketOrderPairing";
                return players[pairedIndex];
            }
        }

        resolutionMethod = "Unavailable";
        return null;
    }

    private static int? InferRoundNumber(IReadOnlyCollection<Participant> players)
    {
        int maxScore = players
            .Select(participant => TryReadPvpInt(participant.Element, "score", out int score) ? score : -1)
            .DefaultIfEmpty(-1)
            .Max();
        return maxScore < 0 ? null : Math.Clamp(maxScore + 1, 1, 3);
    }

    private static EventContext? ReadActiveGacEvent(JsonElement root)
    {
        if (!TryGetProperty(root, "gameEvent", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        EventContext[] contexts =
        [
            .. events.EnumerateArray()
                .Where(IsGacEvent)
                .SelectMany(ReadEventInstances)
        ];
        return contexts
            .Where(context => context.StartTime <= now && now <= context.EndTime)
            .OrderByDescending(context => context.StartTime)
            .FirstOrDefault()
            ?? contexts.OrderByDescending(context => context.StartTime).FirstOrDefault();
    }

    private static IEnumerable<EventContext> ReadEventInstances(JsonElement eventElement)
    {
        string? eventId = ReadString(eventElement, "id");
        if (string.IsNullOrWhiteSpace(eventId) ||
            !TryGetProperty(eventElement, "instance", out JsonElement instances) ||
            instances.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement instance in instances.EnumerateArray())
        {
            string? instanceId = ReadString(instance, "id");
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                continue;
            }

            long startTime = ReadLong(instance, "startTime") ?? long.MinValue;
            long endTime = ReadLong(instance, "endTime") ?? long.MaxValue;
            yield return new EventContext(
                eventId,
                $"{eventId}:{instanceId}",
                startTime,
                endTime,
                eventElement.Clone(),
                instance.Clone());
        }
    }

    private static (GacFormat? Format, string Source) ResolveFormat(GacFormat? formatOverride, string eventId)
    {
        if (formatOverride.HasValue)
        {
            return (formatOverride.Value, "Override");
        }

        if (eventId.Contains("3v3", StringComparison.OrdinalIgnoreCase))
        {
            return (GacFormat.ThreeVsThree, "EventMetadata");
        }

        if (eventId.Contains("5v5", StringComparison.OrdinalIgnoreCase))
        {
            return (GacFormat.FiveVsFive, "EventMetadata");
        }

        int? seasonNumber = ParseSeasonNumber(eventId);
        if (seasonNumber is >= 74)
        {
            return (seasonNumber.Value % 2 == 0 ? GacFormat.FiveVsFive : GacFormat.ThreeVsThree, "SeasonAlternationFallback");
        }

        return (null, "Unavailable");
    }

    private static int? ParseSeasonNumber(string eventId)
    {
        const string marker = "SEASON_";
        int markerIndex = eventId.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        ReadOnlySpan<char> remaining = eventId.AsSpan(markerIndex + marker.Length);
        int length = 0;
        while (length < remaining.Length && char.IsDigit(remaining[length]))
        {
            length++;
        }

        return length > 0 && int.TryParse(remaining[..length], out int value) ? value : null;
    }
}
