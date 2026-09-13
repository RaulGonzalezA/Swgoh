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
    private const int BracketBatchSize = 32;
    private const int RankSearchRadius = 512;
    private const int MaxBracketIndex = 8191;
    private static readonly TimeSpan PositiveCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);

    public async Task<CurrentGacOpponentLookup> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidAllyCode(allyCode))
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
        using JsonDocument arena = await GetPlayerArenaByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);
        string? playerId = ReadString(arena.RootElement, "playerId") ?? ReadString(arena.RootElement, "id");
        SeasonContext? season = ReadCurrentSeason(arena.RootElement);
        GacLeague? league = season?.League ?? ReadLeague(arena.RootElement);

        using JsonDocument events = await PostAsync(
            "getEvents",
            new { payload = new { }, enums = false },
            cancellationToken).ConfigureAwait(false);
        EventContext? activeEvent = ReadActiveGacEvent(events.RootElement, season?.EventInstanceId);

        string? seasonEventInstanceId = NullIfWhiteSpace(season?.EventInstanceId);
        string? eventInstanceId = seasonEventInstanceId ?? activeEvent?.EventInstanceId;
        string? eventId = NullIfWhiteSpace(season?.SeasonId) ?? activeEvent?.EventId;
        if (string.IsNullOrWhiteSpace(eventInstanceId) || string.IsNullOrWhiteSpace(eventId))
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.NoActiveEvent,
                "No active GAC event is currently exposed by Comlink.");
        }

        if (league is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.PlayerNotJoined,
                "The player's current GAC league could not be determined.");
        }

        if (string.IsNullOrWhiteSpace(playerId))
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.PlayerNotJoined,
                "Comlink did not expose the player's internal id required to locate the active GAC bracket.");
        }

        (GacFormat? format, string source) = ResolveFormat(formatOverride, season, activeEvent, eventId);
        if (format is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.FormatUnavailable,
                "The active GAC format could not be detected. Supply format=3v3 or format=5v5 as a single-format override.");
        }

        string[] eventCandidates =
        [
            .. new[] { seasonEventInstanceId, activeEvent?.EventInstanceId }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
        BracketMatch? match = await FindBracketAsync(
            allyCode,
            playerId,
            league.Value,
            season?.Rank,
            eventCandidates,
            cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.PlayerNotJoined,
                $"The player was not found in the active GAC brackets for {league} after scanning the current season instance.");
        }

        Participant? opponent = ResolveOpponent(
            match.Value.Players,
            match.Value.PlayerIndex,
            playerId,
            allyCode,
            out string resolutionMethod);
        if (opponent is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "The bracket was found, but the current opponent could not be resolved from the public bracket data.");
        }

        ParticipantProfile? opponentProfile = await ResolveProfileAsync(opponent, cancellationToken).ConfigureAwait(false);
        if (opponentProfile is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "The opponent was resolved in the bracket, but Comlink did not expose a valid ally code for that player.");
        }

        var currentOpponent = new CurrentGacOpponent(
            allyCode,
            opponentProfile.AllyCode,
            opponentProfile.Name,
            opponentProfile.PlayerId,
            league.Value,
            format.Value,
            eventId,
            match.Value.EventInstanceId,
            match.Value.BracketId,
            activeEvent is null ? null : ReadRoundNumber(activeEvent.Element),
            source,
            resolutionMethod);
        return CurrentGacOpponentLookup.Found(currentOpponent);
    }

    private Task<JsonDocument> GetPlayerArenaByAllyCodeAsync(long allyCode, CancellationToken cancellationToken) =>
        PostAsync(
            "playerArena",
            new
            {
                payload = new { allyCode = allyCode.ToString(), playerDetailsOnly = true },
                enums = false
            },
            cancellationToken);

    private async Task<ParticipantProfile?> ResolveProfileAsync(
        Participant participant,
        CancellationToken cancellationToken)
    {
        if (participant.AllyCode is long allyCode && IsValidAllyCode(allyCode))
        {
            return new ParticipantProfile(allyCode, participant.Name, participant.PlayerId);
        }

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

        if (!TryReadLongProperty(profile.RootElement, "allyCode", out long resolvedAllyCode) ||
            !IsValidAllyCode(resolvedAllyCode))
        {
            return null;
        }

        string name = ReadString(profile.RootElement, "name") ?? participant.Name;
        string? playerId = ReadString(profile.RootElement, "playerId") ??
            ReadString(profile.RootElement, "id") ??
            participant.PlayerId;
        return new ParticipantProfile(resolvedAllyCode, name, playerId);
    }

    private async Task<BracketMatch?> FindBracketAsync(
        long allyCode,
        string playerId,
        GacLeague league,
        int? rank,
        IReadOnlyCollection<string> eventInstanceIds,
        CancellationToken cancellationToken)
    {
        string[] leagueTokens =
        [
            league.ToString().ToUpperInvariant(),
            league.ToString().ToLowerInvariant()
        ];

        foreach (string eventInstanceId in eventInstanceIds)
        {
            foreach (string leagueToken in leagueTokens.Distinct(StringComparer.Ordinal))
            {
                var checkedIndexes = new HashSet<int>();
                if (rank is > 0)
                {
                    int estimatedBracket = Math.Clamp((rank.Value - 1) / BracketSize, 0, MaxBracketIndex);
                    int start = Math.Max(0, estimatedBracket - RankSearchRadius);
                    int end = Math.Min(MaxBracketIndex, estimatedBracket + RankSearchRadius);
                    BracketMatch? nearRank = await ScanRangeAsync(
                        start,
                        end,
                        checkedIndexes,
                        allyCode,
                        playerId,
                        leagueToken,
                        eventInstanceId,
                        cancellationToken).ConfigureAwait(false);
                    if (nearRank is not null)
                    {
                        return nearRank;
                    }
                }

                BracketMatch? fullScan = await ScanRangeAsync(
                    0,
                    MaxBracketIndex,
                    checkedIndexes,
                    allyCode,
                    playerId,
                    leagueToken,
                    eventInstanceId,
                    cancellationToken).ConfigureAwait(false);
                if (fullScan is not null)
                {
                    return fullScan;
                }
            }
        }

        return null;
    }

    private async Task<BracketMatch?> ScanRangeAsync(
        int start,
        int end,
        ISet<int> checkedIndexes,
        long allyCode,
        string playerId,
        string leagueToken,
        string eventInstanceId,
        CancellationToken cancellationToken)
    {
        var pending = new List<int>(BracketBatchSize);
        for (int index = start; index <= end; index++)
        {
            if (!checkedIndexes.Add(index))
            {
                continue;
            }

            pending.Add(index);
            if (pending.Count < BracketBatchSize && index < end)
            {
                continue;
            }

            BracketMatch? match = await ScanBatchAsync(
                pending,
                allyCode,
                playerId,
                leagueToken,
                eventInstanceId,
                cancellationToken).ConfigureAwait(false);
            if (match is not null)
            {
                return match;
            }

            pending.Clear();
        }

        return null;
    }

    private async Task<BracketMatch?> ScanBatchAsync(
        IReadOnlyCollection<int> indexes,
        long allyCode,
        string playerId,
        string leagueToken,
        string eventInstanceId,
        CancellationToken cancellationToken)
    {
        Task<BracketData?>[] requests = indexes
            .Select(index => ReadBracketAsync(index, leagueToken, eventInstanceId, cancellationToken))
            .ToArray();
        BracketData?[] brackets = await Task.WhenAll(requests).ConfigureAwait(false);

        foreach (BracketData? bracket in brackets)
        {
            if (bracket is null)
            {
                continue;
            }

            int playerIndex = Array.FindIndex(bracket.Players, player =>
                string.Equals(player.PlayerId, playerId, StringComparison.Ordinal) ||
                player.AllyCode == allyCode);
            if (playerIndex >= 0)
            {
                return new BracketMatch(eventInstanceId, bracket.BracketId, bracket.Players, playerIndex);
            }
        }

        return null;
    }

    private async Task<BracketData?> ReadBracketAsync(
        int bracketIndex,
        string leagueToken,
        string eventInstanceId,
        CancellationToken cancellationToken)
    {
        string bracketId = $"{eventInstanceId}:{leagueToken}:{bracketIndex}";
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

        Participant[] players =
        [
            .. playersElement.EnumerateArray()
                .Select(ReadParticipant)
                .Where(value => value is not null)
                .Select(value => value!)
        ];
        return players.Length == 0 ? null : new BracketData(bracketId, players);
    }

    private static Participant? ResolveOpponent(
        IReadOnlyList<Participant> players,
        int playerIndex,
        string playerId,
        long allyCode,
        out string resolutionMethod)
    {
        Participant current = players[playerIndex];
        string? opponentPlayerId = FindOpponentIdentifier(current.Element);
        long? opponentAllyCode = FindOpponentAllyCode(current.Element);
        Participant? direct = players.FirstOrDefault(player =>
            !IsSelf(player, playerId, allyCode) &&
            ((opponentAllyCode.HasValue && player.AllyCode == opponentAllyCode.Value) ||
             (!string.IsNullOrWhiteSpace(opponentPlayerId) &&
              string.Equals(player.PlayerId, opponentPlayerId, StringComparison.Ordinal))));
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

    private static bool IsSelf(Participant participant, string playerId, long allyCode) =>
        string.Equals(participant.PlayerId, playerId, StringComparison.Ordinal) || participant.AllyCode == allyCode;

    private static string? FindOpponentIdentifier(JsonElement element)
    {
        foreach ((string name, JsonElement value) in EnumerateProperties(element))
        {
            if (!name.Contains("opponent", StringComparison.OrdinalIgnoreCase) ||
                (!name.EndsWith("playerId", StringComparison.OrdinalIgnoreCase) &&
                 !name.EndsWith("opponentId", StringComparison.OrdinalIgnoreCase)))
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
                .SelectMany(ReadEvents)
        ];
        if (gacEvents.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredEventInstanceId))
        {
            EventContext? exact = gacEvents.FirstOrDefault(value =>
                string.Equals(value.EventInstanceId, preferredEventInstanceId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            string? preferredEventId = EventIdFromInstance(preferredEventInstanceId);
            EventContext? sameSeason = gacEvents.FirstOrDefault(value =>
                string.Equals(value.EventId, preferredEventId, StringComparison.OrdinalIgnoreCase));
            if (sameSeason is not null)
            {
                return sameSeason;
            }
        }

        return gacEvents[0];
    }

    private static IEnumerable<EventContext> ReadEvents(JsonElement element)
    {
        string? eventId = ReadString(element, "id");
        if (string.IsNullOrWhiteSpace(eventId) ||
            !TryGetProperty(element, "instance", out JsonElement instances) ||
            instances.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement instance in instances.EnumerateArray())
        {
            string? instanceId = ReadString(instance, "id");
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                yield return new EventContext(eventId, $"{eventId}:{instanceId}", element.Clone());
            }
        }
    }

    private static bool IsGacEvent(JsonElement element) =>
        TryGetProperty(element, "type", out JsonElement type) &&
        TryReadInt(type, out int value) &&
        value == 10;

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
            .OrderByDescending(status => ParseSeasonNumber(status.SeasonId ?? status.EventInstanceId ?? string.Empty) ?? -1)
            .ThenByDescending(status => status.EventInstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static (GacFormat? Format, string Source) ResolveFormat(
        GacFormat? formatOverride,
        SeasonContext? season,
        EventContext? activeEvent,
        string eventId)
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

        if (activeEvent is not null)
        {
            foreach ((_, JsonElement value) in EnumerateProperties(activeEvent.Element))
            {
                GacFormat? eventFormat = ParseFormat(JsonString(value));
                if (eventFormat.HasValue)
                {
                    return (eventFormat.Value, "EventMetadata");
                }
            }
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
        string? playerId = ReadString(element, "id") ?? ReadString(element, "playerId");
        long? allyCode = TryReadLongProperty(element, "allyCode", out long parsedAllyCode)
            ? parsedAllyCode
            : null;
        if (string.IsNullOrWhiteSpace(playerId) && allyCode is null)
        {
            return null;
        }

        string? name = ReadString(element, "name") ?? ReadString(element, "playerName");
        return new Participant(
            allyCode,
            name ?? playerId ?? allyCode?.ToString() ?? "Unknown",
            playerId,
            element.Clone());
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

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? EventIdFromInstance(string eventInstanceId)
    {
        int separator = eventInstanceId.IndexOf(':');
        return separator > 0 ? eventInstanceId[..separator] : null;
    }

    private static bool IsValidAllyCode(long allyCode) => allyCode is >= 100_000_000 and <= 999_999_999;

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
        long? AllyCode,
        string Name,
        string? PlayerId,
        JsonElement Element);

    private sealed record ParticipantProfile(
        long AllyCode,
        string Name,
        string? PlayerId);

    private sealed record BracketData(
        string BracketId,
        Participant[] Players);

    private readonly record struct BracketMatch(
        string EventInstanceId,
        string BracketId,
        Participant[] Players,
        int PlayerIndex);
}
