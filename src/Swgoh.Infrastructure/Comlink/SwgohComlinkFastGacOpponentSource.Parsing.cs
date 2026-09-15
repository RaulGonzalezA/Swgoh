using System.Diagnostics;
using System.Text.Json;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed partial class SwgohComlinkFastGacOpponentSource
{
    private static int? ReadSkillRating(JsonElement root)
    {
        if (!TryGetProperty(root, "playerRating", out JsonElement playerRating) ||
            !TryGetProperty(playerRating, "playerSkillRating", out JsonElement skill) ||
            !TryGetProperty(skill, "skillRating", out JsonElement rating) ||
            !TryReadInt(rating, out int value))
        {
            return null;
        }

        return value;
    }

    private static GacLeague? ReadLeague(JsonElement root)
    {
        foreach ((string name, JsonElement value) in EnumerateProperties(root))
        {
            if (!name.Equals("league", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("leagueId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryReadInt(value, out int number))
            {
                return number switch
                {
                    20 => GacLeague.Carbonite,
                    40 => GacLeague.Bronzium,
                    60 => GacLeague.Chromium,
                    80 => GacLeague.Aurodium,
                    100 => GacLeague.Kyber,
                    _ => null
                };
            }

            string? text = JsonString(value);
            if (Enum.TryParse(text, true, out GacLeague league) && Enum.IsDefined(league))
            {
                return league;
            }
        }

        return null;
    }

    private static bool TryGetLeaderboardPlayers(JsonElement root, out JsonElement players)
    {
        if (TryGetProperty(root, "player", out players) &&
            players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0)
        {
            return true;
        }

        if (TryGetProperty(root, "leaderboard", out JsonElement leaderboards))
        {
            if (leaderboards.ValueKind == JsonValueKind.Object)
            {
                leaderboards = JsonDocument.Parse($"[{leaderboards.GetRawText()}]").RootElement.Clone();
            }

            if (leaderboards.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement leaderboard in leaderboards.EnumerateArray())
                {
                    if (TryGetProperty(leaderboard, "player", out players) &&
                        players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0)
                    {
                        return true;
                    }
                }
            }
        }

        players = default;
        return false;
    }

    private static Participant? ReadParticipant(JsonElement element)
    {
        string? playerId = ReadString(element, "id") ?? ReadString(element, "playerId");
        long? allyCode = TryReadLongProperty(element, "allyCode", out long parsed) ? parsed : null;
        if (string.IsNullOrWhiteSpace(playerId) && allyCode is null)
        {
            return null;
        }

        return new Participant(
            allyCode,
            ReadString(element, "name") ?? ReadString(element, "playerName") ?? playerId ?? allyCode?.ToString() ?? "Unknown",
            playerId,
            element.Clone());
    }

    private static int? ReadRoundNumber(JsonElement element)
    {
        foreach ((string name, JsonElement value) in EnumerateProperties(element))
        {
            if ((name.Equals("round", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("roundNumber", StringComparison.OrdinalIgnoreCase)) &&
                TryReadInt(value, out int round) && round is >= 1 and <= 3)
            {
                return round;
            }
        }

        return null;
    }

    private static bool TryReadPvpInt(JsonElement participant, string propertyName, out int value)
    {
        value = default;
        return TryGetProperty(participant, "pvpStatus", out JsonElement pvpStatus) &&
               TryGetProperty(pvpStatus, propertyName, out JsonElement property) &&
               TryReadInt(property, out value);
    }

    private static IEnumerable<int> BuildProximityIndexes(int center, int radius)
    {
        yield return center;
        for (int delta = 1; delta <= radius; delta++)
        {
            yield return center - delta;
            yield return center + delta;
        }
    }

    private bool TryGetCachedLocation(string key, out int index)
    {
        index = default;
        if (!locationCache.TryGetValue(key, out LocationCacheEntry? cached))
        {
            return false;
        }

        if (cached.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            locationCache.TryRemove(key, out _);
            return false;
        }

        index = cached.Index;
        return true;
    }

    private static TimeSpan GetBatchDelay(LookupDiagnostics diagnostics) =>
        diagnostics.RateLimits > 0 ? RateLimitedBatchDelay : BatchDelay;

    private static TimeSpan GetRateLimitDelay(int attempt, int index)
    {
        double delay = Math.Min(2_000, 150 * (1 << attempt));
        return TimeSpan.FromMilliseconds(delay + ((index % BracketBatchSize) * 20));
    }

    private static string LocationKey(long allyCode, string eventInstanceId, string leagueToken) =>
        $"{allyCode}:{eventInstanceId}:{leagueToken}";

    private static string LastKnownKey(long allyCode, string leagueToken) => $"{allyCode}:{leagueToken}";

    private static bool IsGacEvent(JsonElement element) =>
        TryGetProperty(element, "type", out JsonElement type) && TryReadInt(type, out int value) && value == 10;

    private static IEnumerable<(string Name, JsonElement Value)> EnumerateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                yield return (property.Name, property.Value);
                foreach ((string name, JsonElement value) in EnumerateProperties(property.Value))
                {
                    yield return (name, value);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                foreach ((string name, JsonElement value) in EnumerateProperties(item))
                {
                    yield return (name, value);
                }
            }
        }
    }

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

    private static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) ? JsonString(value) : null;

    private static long? ReadLong(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) && TryReadLong(value, out long result) ? result : null;

    private static string? JsonString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    private static bool TryReadInt(JsonElement value, out int result)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result))
        {
            return true;
        }

        return int.TryParse(JsonString(value), out result);
    }

    private static bool TryReadLongProperty(JsonElement element, string name, out long result)
    {
        result = default;
        return TryGetProperty(element, name, out JsonElement value) && TryReadLong(value, out result);
    }

    private static bool TryReadLong(JsonElement value, out long result)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result))
        {
            return true;
        }

        return long.TryParse(JsonString(value), out result);
    }

    public void Dispose()
    {
        resultCache.Dispose();
        locationCache.Dispose();
        lastKnownBracketIndexes.Dispose();
        ratingSamples.Dispose();
    }

    private sealed record ResultCacheEntry(CurrentGacOpponentLookup Lookup, DateTimeOffset ExpiresAtUtc);
    private sealed record LocationCacheEntry(int Index, DateTimeOffset ExpiresAtUtc);
    private sealed record RatingSampleCacheEntry(int SkillRating, DateTimeOffset ExpiresAtUtc);
    private sealed record Participant(long? AllyCode, string Name, string? PlayerId, JsonElement Element);
    private sealed record ParticipantProfile(long AllyCode, string Name, string? PlayerId);
    private sealed record BracketData(int Index, string BracketId, Participant[] Players);
    private readonly record struct BracketMatch(string BracketId, int Index, Participant[] Players, int PlayerIndex);
    private sealed record EventContext(
        string EventId,
        string EventInstanceId,
        long StartTime,
        long EndTime,
        JsonElement EventElement,
        JsonElement InstanceElement);

    private sealed class LookupDiagnostics
    {
        public Stopwatch Elapsed { get; } = Stopwatch.StartNew();
        public int? SkillRating { get; set; }
        public string? League { get; set; }
        public int? RatingCenter { get; set; }
        public bool LocationCacheHit { get; set; }
        public int LeaderboardRequests { get; set; }
        public int ArenaRequests { get; set; }
        public int RateLimits { get; set; }
        public string? BracketId { get; set; }
    }
}
