using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class SwgohComlinkCurrentRoundOpponentSource(
    SwgohComlinkGacOpponentSource bracketSource,
    IHttpClientFactory httpClientFactory) : ICurrentGacOpponentSource
{
    private const int RateLimitRetryCount = 5;

    public async Task<CurrentGacOpponentLookup> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken = default)
    {
        CurrentGacOpponentLookup lookup = await bracketSource
            .GetAsync(allyCode, formatOverride, cancellationToken)
            .ConfigureAwait(false);

        CurrentGacOpponent? current = lookup.Opponent;
        if (lookup.Status != CurrentGacOpponentStatus.Found ||
            current is null ||
            !string.Equals(
                current.OpponentResolutionMethod,
                "BracketOrderPairing",
                StringComparison.Ordinal))
        {
            return lookup;
        }

        using JsonDocument player = await PostAsync(
            "playerArena",
            new
            {
                payload = new { allyCode = allyCode.ToString(), playerDetailsOnly = true },
                enums = false
            },
            cancellationToken).ConfigureAwait(false);
        string? playerId = ReadString(player.RootElement, "playerId") ?? ReadString(player.RootElement, "id");
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return lookup;
        }

        Participant[]? participants = await ReadBracketAsync(current, cancellationToken).ConfigureAwait(false);
        if (participants is null)
        {
            return lookup;
        }

        Participant? resolved = ResolveFromSwissStandings(participants, playerId);
        if (resolved is null || string.IsNullOrWhiteSpace(resolved.PlayerId))
        {
            return lookup;
        }

        ParticipantProfile? profile = await ResolveProfileAsync(resolved, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return lookup;
        }

        return CurrentGacOpponentLookup.Found(current with
        {
            OpponentAllyCode = profile.AllyCode,
            OpponentName = profile.Name,
            OpponentPlayerId = profile.PlayerId,
            OpponentResolutionMethod = "PvpScoreRankPairing"
        });
    }

    private async Task<Participant[]?> ReadBracketAsync(
        CurrentGacOpponent current,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            payload = new
            {
                leaderboardType = 4,
                eventInstanceId = current.EventInstanceId,
                groupId = current.BracketId
            },
            enums = false
        };

        HttpClient client = httpClientFactory.CreateClient(SwgohComlinkGacOpponentSource.HttpClientName);
        for (int attempt = 0; attempt <= RateLimitRetryCount; attempt++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "getLeaderboard",
                request,
                cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                if (body.Contains("Rate exceeded", StringComparison.OrdinalIgnoreCase) &&
                    attempt < RateLimitRetryCount)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(Math.Min(2_000, 150 * (1 << attempt))),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return null;
            }

            response.EnsureSuccessStatusCode();
            using JsonDocument document = JsonDocument.Parse(body);
            if (!TryGetLeaderboardPlayers(document.RootElement, out JsonElement playersElement))
            {
                return null;
            }

            Participant[] participants =
            [
                .. playersElement.EnumerateArray()
                    .Select(ReadParticipant)
                    .Where(participant => participant is not null)
                    .Select(participant => participant!)
            ];
            return participants.Length == 0 ? null : participants;
        }

        return null;
    }

    private async Task<ParticipantProfile?> ResolveProfileAsync(
        Participant participant,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(participant.PlayerId))
        {
            return null;
        }

        using JsonDocument profile = await PostAsync(
            "playerArena",
            new
            {
                payload = new { playerId = participant.PlayerId, playerDetailsOnly = true },
                enums = false
            },
            cancellationToken).ConfigureAwait(false);

        if (!TryReadLongProperty(profile.RootElement, "allyCode", out long allyCode) ||
            allyCode is < 100_000_000 or > 999_999_999)
        {
            return null;
        }

        string name = ReadString(profile.RootElement, "name") ?? participant.Name;
        string? playerId = ReadString(profile.RootElement, "playerId") ??
            ReadString(profile.RootElement, "id") ??
            participant.PlayerId;
        return new ParticipantProfile(allyCode, name, playerId);
    }

    private static Participant? ResolveFromSwissStandings(
        IReadOnlyCollection<Participant> participants,
        string playerId)
    {
        Participant? current = participants.FirstOrDefault(participant =>
            string.Equals(participant.PlayerId, playerId, StringComparison.Ordinal));
        if (current is null || !TryReadPvpValue(current.Element, "score", out int currentScore))
        {
            return null;
        }

        Participant[] sameRecord =
        [
            .. participants.Where(participant =>
                TryReadPvpValue(participant.Element, "score", out int score) &&
                score == currentScore)
        ];

        // After round one, GAC Swiss groups contain either two or four players with the same record.
        // Avoid guessing the initial round when all eight players can still have score zero.
        if (sameRecord.Length is not (2 or 4))
        {
            return null;
        }

        var ranked = new List<RankedParticipant>(sameRecord.Length);
        foreach (Participant participant in sameRecord)
        {
            if (!TryReadPvpValue(participant.Element, "rank", out int rank))
            {
                return null;
            }

            ranked.Add(new RankedParticipant(participant, rank));
        }

        if (ranked.Select(value => value.Rank).Distinct().Count() != ranked.Count)
        {
            return null;
        }

        RankedParticipant[] ordered = [.. ranked.OrderBy(value => value.Rank)];
        int currentIndex = Array.FindIndex(ordered, value =>
            string.Equals(value.Participant.PlayerId, playerId, StringComparison.Ordinal));
        if (currentIndex < 0)
        {
            return null;
        }

        int pairedIndex = ordered.Length - 1 - currentIndex;
        return pairedIndex == currentIndex ? null : ordered[pairedIndex].Participant;
    }

    private async Task<JsonDocument> PostAsync(
        string path,
        object request,
        CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(SwgohComlinkGacOpponentSource.HttpClientName);
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static bool TryGetLeaderboardPlayers(JsonElement root, out JsonElement players)
    {
        if (TryGetProperty(root, "player", out players) &&
            players.ValueKind == JsonValueKind.Array &&
            players.GetArrayLength() > 0)
        {
            return true;
        }

        if (TryGetProperty(root, "leaderboard", out JsonElement leaderboards) &&
            leaderboards.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement leaderboard in leaderboards.EnumerateArray())
            {
                if (TryGetProperty(leaderboard, "player", out players) &&
                    players.ValueKind == JsonValueKind.Array &&
                    players.GetArrayLength() > 0)
                {
                    return true;
                }
            }
        }

        players = default;
        return false;
    }

    private static Participant? ReadParticipant(JsonElement element)
    {
        string? playerId = ReadString(element, "id") ?? ReadString(element, "playerId");
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        string name = ReadString(element, "name") ?? ReadString(element, "playerName") ?? playerId;
        return new Participant(name, playerId, element.Clone());
    }

    private static bool TryReadPvpValue(JsonElement participant, string name, out int value)
    {
        value = default;
        return TryGetProperty(participant, "pvpStatus", out JsonElement pvpStatus) &&
               TryGetProperty(pvpStatus, name, out JsonElement property) &&
               TryReadInt(property, out value);
    }

    private static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) ? JsonString(value) : null;

    private static bool TryReadLongProperty(JsonElement element, string name, out long value)
    {
        value = default;
        return TryGetProperty(element, name, out JsonElement property) &&
               long.TryParse(JsonString(property), out value);
    }

    private static bool TryReadInt(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }

        return int.TryParse(JsonString(element), out value);
    }

    private static string? JsonString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private sealed record Participant(string Name, string PlayerId, JsonElement Element);

    private sealed record ParticipantProfile(long AllyCode, string Name, string? PlayerId);

    private sealed record RankedParticipant(Participant Participant, int Rank);
}
