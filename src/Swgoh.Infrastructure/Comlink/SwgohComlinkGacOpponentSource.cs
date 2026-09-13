using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class SwgohComlinkGacOpponentSource(IHttpClientFactory httpClientFactory) : ICurrentGacOpponentSource
{
    internal const string HttpClientName = "SwgohComlinkGac";

    private const int BracketSize = 8;
    private const int EstimatedBracketRadius = 96;
    private const int FallbackBracketLimit = 2048;
    private static readonly TimeSpan PositiveCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);

    public async Task<CurrentGacOpponentLookup> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken = default)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

        string cacheKey = $"{allyCode}:{formatOverride?.ToString() ?? "auto"}";
        if (cache.TryGetValue(cacheKey, out CacheEntry? cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.Lookup;
        }

        CurrentGacOpponentLookup lookup = await ResolveAsync(allyCode, formatOverride, cancellationToken)
            .ConfigureAwait(false);
        TimeSpan duration = lookup.Status == CurrentGacOpponentStatus.Found
            ? PositiveCacheDuration
            : NegativeCacheDuration;
        cache[cacheKey] = new CacheEntry(lookup, DateTimeOffset.UtcNow.Add(duration));
        return lookup;
    }

    private async Task<CurrentGacOpponentLookup> ResolveAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken)
    {
        using JsonDocument arena = await PostAsync(
            "playerArena",
            new
            {
                payload = new { allyCode = allyCode.ToString(), playerDetailsOnly = true },
                enums = false
            },
            cancellationToken).ConfigureAwait(false);

        string? playerId = ReadString(arena.RootElement, "playerId");
        GacLeague? league = ReadLeague(arena.RootElement);
        SeasonContext? season = ReadCurrentSeason(arena.RootElement);

        using JsonDocument events = await PostAsync(
            "getEvents",
            new { payload = new { }, enums = false },
            cancellationToken).ConfigureAwait(false);
        EventContext? activeEvent = ReadActiveGacEvent(events.RootElement, season?.EventInstanceId);
        if (activeEvent is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.NoActiveEvent,
                "No active GAC event is currently exposed by Comlink.");
        }

        league ??= season?.League;
        if (league is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.PlayerNotJoined,
                "The player's current GAC league could not be determined.");
        }

        (GacFormat? format, string source) = ResolveFormat(formatOverride, season, activeEvent);
        if (format is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.FormatUnavailable,
                "The active GAC format could not be detected. Supply format=3v3 or format=5v5 as a single-format override.");
        }

        BracketMatch? match = await FindBracketAsync(
            allyCode,
            playerId,
            league.Value,
            activeEvent.EventInstanceId,
            season?.Rank,
            cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.PlayerNotJoined,
                "The player was not found in the active GAC brackets for the detected league.");
        }

        Participant? opponent = ResolveOpponent(match.Value.Players, match.Value.PlayerIndex, playerId, allyCode, out string resolutionMethod);
        if (opponent is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "The bracket was found, but the current opponent could not be resolved from the public bracket data.");
        }

        var currentOpponent = new CurrentGacOpponent(
            allyCode,
            opponent.AllyCode,
            opponent.Name,
            opponent.PlayerId,
            league.Value,
            format.Value,
            activeEvent.EventId,
            activeEvent.EventInstanceId,
            match.Value.BracketId,
            ReadRoundNumber(activeEvent.Element),
            source,
            resolutionMethod);
        return CurrentGacOpponentLookup.Found(currentOpponent);
    }

    private async Task<BracketMatch?> FindBracketAsync(
        long allyCode,
        string? playerId,
        GacLeague league,
        string eventInstanceId,
        int? rank,
        CancellationToken cancellationToken)
    {
        var checkedIndexes = new HashSet<int>();
        if (rank is > 0)
        {
            int estimated = Math.Max(0, (rank.Value - 1) / BracketSize);
            int start = Math.Max(0, estimated - EstimatedBracketRadius);
            int end = estimated + EstimatedBracketRadius;
            for (int index = start; index <= end; index++)
            {
                checkedIndexes.Add(index);
                BracketMatch? match = await TryBracketAsync(
                    index,
                    allyCode,
                    playerId,
                    league,
                    eventInstanceId,
                    cancellationToken).ConfigureAwait(false);
                if (match is not null)
                {
                    return match;
                }
            }
        }

        for (int index = 0; index < FallbackBracketLimit; index++)
        {
            if (!checkedIndexes.Add(index))
            {
                continue;
            }

            BracketMatch? match = await TryBracketAsync(
                index,
                allyCode,
                playerId,
                league,
                eventInstanceId,
                cancellationToken).ConfigureAwait(false);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private async Task<BracketMatch?> TryBracketAsync(
        int bracketIndex,
        long allyCode,
        string? playerId,
        GacLeague league,
        string eventInstanceId,
        CancellationToken cancellationToken)
    {
        string leagueName = league.ToString().ToUpperInvariant();
        string bracketId = $"{eventInstanceId}:{leagueName}:{bracketIndex}";
        using JsonDocument response = await PostAsync(
            "getLeaderboard",
            new
            {
                payload = new
                {
                    leaderboardType = 4,
                    eventInstanceId,
                    groupId = bracketId
                },
                enums = false
            },
            cancellationToken).ConfigureAwait(false);

        if (!TryGetProperty(response.RootElement, "player", out JsonElement playersElement) ||
            playersElement.ValueKind != JsonValueKind.Array ||
            playersElement.GetArrayLength() == 0)
        {
            return null;
        }

        Participant[] players = [.. playersElement.EnumerateArray().Select(ReadParticipant).Where(value => value is not null).Select(value => value!)];
        int playerIndex = Array.FindIndex(players, player =>
            player.AllyCode == allyCode ||
            (!string.IsNullOrWhiteSpace(playerId) && string.Equals(player.PlayerId, playerId, StringComparison.Ordinal)));
        return playerIndex < 0 ? null : new BracketMatch(bracketId, players, playerIndex);
    }

    private static Participant? ResolveOpponent(
        IReadOnlyList<Participant> players,
        int playerIndex,
        string? playerId,
        long allyCode,
        out string resolutionMethod)
    {
        Participant current = players[playerIndex];
        string? opponentPlayerId = FindOpponentIdentifier(current.Element, "playerId");
        long? opponentAllyCode = FindOpponentAllyCode(current.Element);
        Participant? direct = players.FirstOrDefault(player =>
            player.AllyCode != allyCode &&
            ((opponentAllyCode.HasValue && player.AllyCode == opponentAllyCode.Value) ||
             (!string.IsNullOrWhiteSpace(opponentPlayerId) && string.Equals(player.PlayerId, opponentPlayerId, StringComparison.Ordinal))));
        if (direct is not null)
        {
            resolutionMethod = "DirectBracketMetadata";
            return direct;
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

    private static string? FindOpponentIdentifier(JsonElement element, string suffix)
    {
        foreach ((string name, JsonElement value) in EnumerateProperties(element))
        {
            if (!name.Contains("opponent", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? result = JsonString(value);
            if (!string.IsNullOrWhiteSpace(result))
            {
                return result;
            }
        }

        return null;
    }

    private static long? FindOpponentAllyCode(JsonElement element)
    {
        foreach ((string name, JsonElement value) in EnumerateProperties(element))
        {
            if (!name.Contains("opponent", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("ally", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryReadLong(value, out long allyCode))
            {
                return allyCode;
            }
        }

        return null;
    }

    private async Task<JsonDocument> PostAsync(string path, object request, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static EventContext? ReadActiveGacEvent(JsonElement root, string? preferredEventInstanceId)
    {
        if (!TryGetProperty(root, "gameEvent", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        EventContext[] gacEvents =
        [
            .. events.EnumerateArray()
                .Where(IsGacEvent)
                .Select(ReadEvent)
                .Where(value => value is not null)
                .Select(value => value!)
        ];
        if (gacEvents.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredEventInstanceId))
        {
            EventContext? preferred = gacEvents.FirstOrDefault(value =>
                string.Equals(value.EventInstanceId, preferredEventInstanceId, StringComparison.OrdinalIgnoreCase) ||
                preferredEventInstanceId.Contains(value.EventId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return gacEvents[0];
    }

    private static bool IsGacEvent(JsonElement element) =>
        TryGetProperty(element, "type", out JsonElement type) &&
        TryReadInt(type, out int value) &&
        value == 10;

    private static EventContext? ReadEvent(JsonElement element)
    {
        string? eventId = ReadString(element, "id");
        if (string.IsNullOrWhiteSpace(eventId) ||
            !TryGetProperty(element, "instance", out JsonElement instances) ||
            instances.ValueKind != JsonValueKind.Array ||
            instances.GetArrayLength() == 0)
        {
            return null;
        }

        string? instanceId = ReadString(instances[0], "id");
        return string.IsNullOrWhiteSpace(instanceId)
            ? null
            : new EventContext(eventId, $"{eventId}:{instanceId}", element);
    }

    private static SeasonContext? ReadCurrentSeason(JsonElement root)
    {
        if (!TryGetProperty(root, "seasonStatus", out JsonElement statuses) || statuses.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return statuses.EnumerateArray()
            .Select(status => new SeasonContext(
                ReadString(status, "seasonId"),
                ReadString(status, "eventInstanceId"),
                ReadLeague(status),
                ReadInt(status, "rank")))
            .OrderByDescending(status => status.EventInstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static (GacFormat? Format, string Source) ResolveFormat(
        GacFormat? formatOverride,
        SeasonContext? season,
        EventContext activeEvent)
    {
        if (formatOverride.HasValue)
        {
            return (formatOverride.Value, "Override");
        }

        GacFormat? seasonFormat = ParseFormat(season?.SeasonId);
        if (seasonFormat.HasValue)
        {
            return (seasonFormat.Value, "SeasonStatus");
        }

        foreach ((_, JsonElement value) in EnumerateProperties(activeEvent.Element))
        {
            GacFormat? eventFormat = ParseFormat(JsonString(value));
            if (eventFormat.HasValue)
            {
                return (eventFormat.Value, "EventMetadata");
            }
        }

        int? seasonNumber = ParseSeasonNumber(activeEvent.EventId);
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

        return length > 0 && int.TryParse(remaining[..length], out int season) ? season : null;
    }

    private static GacFormat? ParseFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Contains("3v3", StringComparison.OrdinalIgnoreCase))
        {
            return GacFormat.ThreeVsThree;
        }

        if (value.Contains("5v5", StringComparison.OrdinalIgnoreCase))
        {
            return GacFormat.FiveVsFive;
        }

        return null;
    }

    private static GacLeague? ReadLeague(JsonElement element)
    {
        JsonElement searchRoot = element;
        if (TryGetProperty(element, "playerRating", out JsonElement playerRating))
        {
            searchRoot = playerRating;
        }

        foreach ((string name, JsonElement value) in EnumerateProperties(searchRoot))
        {
            if (!name.Equals("league", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("leagueId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            GacLeague? league = ParseLeague(value);
            if (league.HasValue)
            {
                return league;
            }
        }

        return null;
    }

    private static GacLeague? ParseLeague(JsonElement value)
    {
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
        return Enum.TryParse(text, ignoreCase: true, out GacLeague league) && Enum.IsDefined(league)
            ? league
            : null;
    }

    private static Participant? ReadParticipant(JsonElement element)
    {
        if (!TryReadLongProperty(element, "allyCode", out long allyCode))
        {
            return null;
        }

        string? name = ReadString(element, "name") ?? ReadString(element, "playerName");
        string? playerId = ReadString(element, "playerId");
        return new Participant(allyCode, name ?? allyCode.ToString(), playerId, element.Clone());
    }

    private static int? ReadRoundNumber(JsonElement element)
    {
        foreach ((string name, JsonElement value) in EnumerateProperties(element))
        {
            if ((name.Equals("round", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("roundNumber", StringComparison.OrdinalIgnoreCase)) &&
                TryReadInt(value, out int round) &&
                round is >= 1 and <= 3)
            {
                return round;
            }
        }

        return null;
    }

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

    private static string? JsonString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    private static int? ReadInt(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) && TryReadInt(value, out int result) ? result : null;

    private static bool TryReadInt(JsonElement value, out int result)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result))
        {
            return true;
        }

        return int.TryParse(JsonString(value), out result);
    }

    private static bool TryReadLongProperty(JsonElement element, string name, out long result) =>
        TryGetProperty(element, name, out JsonElement value) && TryReadLong(value, out result);

    private static bool TryReadLong(JsonElement value, out long result)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result))
        {
            return true;
        }

        return long.TryParse(JsonString(value), out result);
    }

    private sealed record CacheEntry(CurrentGacOpponentLookup Lookup, DateTimeOffset ExpiresAtUtc);

    private sealed record SeasonContext(
        string? SeasonId,
        string? EventInstanceId,
        GacLeague? League,
        int? Rank);

    private sealed record EventContext(
        string EventId,
        string EventInstanceId,
        JsonElement Element);

    private sealed record Participant(
        long AllyCode,
        string Name,
        string? PlayerId,
        JsonElement Element);

    private readonly record struct BracketMatch(
        string BracketId,
        Participant[] Players,
        int PlayerIndex);
}
