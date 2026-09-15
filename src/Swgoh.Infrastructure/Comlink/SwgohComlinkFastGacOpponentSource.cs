using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Polly.Timeout;

using Swgoh.Application.Caching;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class SwgohComlinkFastGacOpponentSource(
    IHttpClientFactory httpClientFactory,
    ILogger<SwgohComlinkFastGacOpponentSource> logger) : ICurrentGacOpponentSource, IDisposable
{
    private const int BracketSize = 8;
    private const int BracketBatchSize = 8;
    private const int MaxBracketIndex = 20_000;
    private const int RateLimitRetryCount = 5;
    private const long ResultCacheSizeLimit = 2_048;
    private const long LocationCacheSizeLimit = 4_096;
    private const long LastKnownBracketCacheSizeLimit = 2_048;
    private const long RatingSampleCacheSizeLimit = 8_192;
    private static readonly int[] LocalSearchRadii = [16, 48, 128];
    private static readonly TimeSpan BatchDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RateLimitedBatchDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan ResultCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LocationCacheDuration = TimeSpan.FromDays(8);
    private static readonly TimeSpan RatingSampleCacheDuration = TimeSpan.FromDays(1);
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(30);

    private readonly BoundedMemoryCache<string, ResultCacheEntry> resultCache = new(
        ResultCacheSizeLimit,
        absoluteExpirationSelector: static entry => entry.ExpiresAtUtc);
    private readonly BoundedMemoryCache<string, LocationCacheEntry> locationCache = new(
        LocationCacheSizeLimit,
        absoluteExpirationSelector: static entry => entry.ExpiresAtUtc);
    private readonly BoundedMemoryCache<string, int> lastKnownBracketIndexes = new(
        LastKnownBracketCacheSizeLimit,
        defaultLifetime: LocationCacheDuration);
    private readonly BoundedMemoryCache<string, RatingSampleCacheEntry> ratingSamples = new(
        RatingSampleCacheSizeLimit,
        absoluteExpirationSelector: static entry => entry.ExpiresAtUtc);

    public async Task<CurrentGacOpponentLookup> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken = default)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

        string resultKey = $"{allyCode}:{formatOverride?.ToString() ?? "auto"}";
        if (resultCache.TryGetValue(resultKey, out ResultCacheEntry? cached) &&
            cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.Lookup;
        }

        var diagnostics = new LookupDiagnostics();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LookupTimeout);

        CurrentGacOpponentLookup lookup;
        try
        {
            lookup = await ResolveAsync(allyCode, formatOverride, diagnostics, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            lookup = CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "La búsqueda rápida del rival de Gran Arena ha agotado 30 segundos. Inténtalo de nuevo; si ya localizamos tu bracket, la siguiente consulta será directa.");
        }
        catch (TimeoutRejectedException) when (!cancellationToken.IsCancellationRequested)
        {
            lookup = CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "Comlink ha tardado demasiado en responder durante la búsqueda del rival de Gran Arena.");
        }
        finally
        {
            logger.LogInformation(
                "Fast GAC lookup for {AllyCode}: Skill {SkillRating}, League {League}, RatingCenter {RatingCenter}, LocationCache {LocationCacheHit}, LeaderboardRequests {LeaderboardRequests}, ArenaRequests {ArenaRequests}, RateLimits {RateLimits}, Bracket {BracketId}, Total {ElapsedMs} ms",
                allyCode,
                diagnostics.SkillRating,
                diagnostics.League,
                diagnostics.RatingCenter,
                diagnostics.LocationCacheHit,
                diagnostics.LeaderboardRequests,
                diagnostics.ArenaRequests,
                diagnostics.RateLimits,
                diagnostics.BracketId,
                diagnostics.Elapsed.Elapsed.TotalMilliseconds);
        }

        TimeSpan cacheDuration = lookup.Status == CurrentGacOpponentStatus.Found
            ? ResultCacheDuration
            : NegativeCacheDuration;
        resultCache[resultKey] = new ResultCacheEntry(lookup, DateTimeOffset.UtcNow.Add(cacheDuration));
        return lookup;
    }

    private async Task<CurrentGacOpponentLookup> ResolveAsync(
        long allyCode,
        GacFormat? formatOverride,
        LookupDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        using JsonDocument selfProfile = await GetArenaProfileAsync(
            new { allyCode = allyCode.ToString(), playerDetailsOnly = true },
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        string? playerId = ReadString(selfProfile.RootElement, "playerId") ?? ReadString(selfProfile.RootElement, "id");
        GacLeague? league = ReadLeague(selfProfile.RootElement);
        int? skillRating = ReadSkillRating(selfProfile.RootElement);
        diagnostics.League = league?.ToString();
        diagnostics.SkillRating = skillRating;

        if (string.IsNullOrWhiteSpace(playerId))
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.PlayerNotJoined,
                "Comlink no ha expuesto el identificador interno del jugador necesario para localizar su bracket de Gran Arena.");
        }

        if (league is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.PlayerNotJoined,
                "No se ha podido determinar la liga actual de Gran Arena del jugador.");
        }

        if (skillRating is null or <= 0)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "Comlink no ha expuesto el skill rating necesario para localizar rápidamente el bracket de Gran Arena.");
        }

        using JsonDocument events = await PostAsync(
            "getEvents",
            new { payload = new { }, enums = false },
            cancellationToken).ConfigureAwait(false);
        EventContext? activeEvent = ReadActiveGacEvent(events.RootElement);
        if (activeEvent is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.NoActiveEvent,
                "No hay una instancia activa de Gran Arena expuesta por Comlink en este momento.");
        }

        (GacFormat? format, string formatSource) = ResolveFormat(formatOverride, activeEvent.EventId);
        if (format is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.FormatUnavailable,
                "No se ha podido detectar el formato de la Gran Arena activa. Usa format=3v3 o format=5v5 como override.");
        }

        string leagueToken = league.Value.ToString().ToUpperInvariant();
        BracketMatch? match = await FindBracketAsync(
            allyCode,
            playerId,
            skillRating.Value,
            leagueToken,
            activeEvent.EventInstanceId,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "No se ha localizado tu bracket cerca del skill rating actual. La búsqueda se ha detenido para evitar un barrido de varios minutos.");
        }

        Participant? opponent = ResolveCurrentOpponent(
            match.Value.Players,
            match.Value.PlayerIndex,
            playerId,
            allyCode,
            out string resolutionMethod);
        if (opponent is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "Se ha localizado el bracket, pero no se ha podido determinar el rival de la ronda actual.");
        }

        ParticipantProfile? opponentProfile = await ResolveProfileAsync(opponent, diagnostics, cancellationToken).ConfigureAwait(false);
        if (opponentProfile is null)
        {
            return CurrentGacOpponentLookup.Unavailable(
                CurrentGacOpponentStatus.OpponentUnavailable,
                "Se ha localizado el rival, pero Comlink no ha expuesto un ally code válido para su perfil.");
        }

        int? roundNumber = ReadRoundNumber(activeEvent.InstanceElement) ??
            ReadRoundNumber(activeEvent.EventElement) ??
            InferRoundNumber(match.Value.Players);

        return CurrentGacOpponentLookup.Found(new CurrentGacOpponent(
            allyCode,
            opponentProfile.AllyCode,
            opponentProfile.Name,
            opponentProfile.PlayerId,
            league.Value,
            format.Value,
            activeEvent.EventId,
            activeEvent.EventInstanceId,
            match.Value.BracketId,
            roundNumber,
            formatSource,
            resolutionMethod));
    }

    private async Task<BracketMatch?> FindBracketAsync(
        long allyCode,
        string playerId,
        int skillRating,
        string leagueToken,
        string eventInstanceId,
        LookupDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        string locationKey = LocationKey(allyCode, eventInstanceId, leagueToken);
        var checkedIndexes = new HashSet<int>();
        var requestCache = new ConcurrentDictionary<int, Task<BracketData?>>();

        Task<BracketData?> GetBracketAsync(int index) => requestCache.GetOrAdd(
            index,
            value => ReadBracketAsync(value, leagueToken, eventInstanceId, diagnostics, cancellationToken));

        if (TryGetCachedLocation(locationKey, out int cachedIndex))
        {
            diagnostics.LocationCacheHit = true;
            BracketMatch? cached = await CheckIndexesAsync(
                [cachedIndex],
                checkedIndexes,
                allyCode,
                playerId,
                GetBracketAsync,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return CacheMatch(cached.Value, allyCode, eventInstanceId, leagueToken);
            }

            locationCache.TryRemove(locationKey, out _);
        }

        string lastKnownKey = LastKnownKey(allyCode, leagueToken);
        if (lastKnownBracketIndexes.TryGetValue(lastKnownKey, out int lastKnown) &&
            lastKnown is >= 0 and <= MaxBracketIndex)
        {
            BracketMatch? previous = await CheckIndexesAsync(
                [lastKnown],
                checkedIndexes,
                allyCode,
                playerId,
                GetBracketAsync,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
            if (previous is not null)
            {
                return CacheMatch(previous.Value, allyCode, eventInstanceId, leagueToken);
            }
        }

        int? center = await LocateBySkillRatingAsync(
            skillRating,
            leagueToken,
            eventInstanceId,
            GetBracketAsync,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        if (center is null)
        {
            return null;
        }

        diagnostics.RatingCenter = center;
        foreach (int radius in LocalSearchRadii)
        {
            int[] indexes = BuildProximityIndexes(center.Value, radius)
                .Where(index => index is >= 0 and <= MaxBracketIndex)
                .ToArray();
            BracketMatch? match = await CheckIndexesAsync(
                indexes,
                checkedIndexes,
                allyCode,
                playerId,
                GetBracketAsync,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
            if (match is not null)
            {
                return CacheMatch(match.Value, allyCode, eventInstanceId, leagueToken);
            }
        }

        return null;
    }

    private async Task<int?> LocateBySkillRatingAsync(
        int targetSkillRating,
        string leagueToken,
        string eventInstanceId,
        Func<int, Task<BracketData?>> getBracketAsync,
        LookupDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        int low = 0;
        int high = 256;

        while (high < MaxBracketIndex)
        {
            BracketData? bracket = await getBracketAsync(high).ConfigureAwait(false);
            if (bracket is null)
            {
                break;
            }

            low = high;
            high = Math.Min(MaxBracketIndex, high * 2);
            if (high == low)
            {
                break;
            }
        }

        if (high == MaxBracketIndex && await getBracketAsync(high).ConfigureAwait(false) is not null)
        {
            low = 0;
        }
        else
        {
            low = 0;
            high = Math.Max(0, high - 1);
        }

        int? bestIndex = null;
        int bestDelta = int.MaxValue;
        int iterations = 0;
        while (low <= high && iterations++ < 18)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = low + ((high - low) / 2);
            int? sampleRating = await ReadBracketSkillRatingAsync(
                index,
                leagueToken,
                eventInstanceId,
                getBracketAsync,
                diagnostics,
                cancellationToken).ConfigureAwait(false);

            if (sampleRating is null)
            {
                high = index - 1;
                continue;
            }

            int delta = Math.Abs(sampleRating.Value - targetSkillRating);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestIndex = index;
            }

            if (sampleRating.Value == targetSkillRating)
            {
                return index;
            }

            // GAC brackets are ordered by descending skill rating: higher index => lower rating.
            if (sampleRating.Value > targetSkillRating)
            {
                low = index + 1;
            }
            else
            {
                high = index - 1;
            }
        }

        return bestIndex;
    }

    private async Task<int?> ReadBracketSkillRatingAsync(
        int index,
        string leagueToken,
        string eventInstanceId,
        Func<int, Task<BracketData?>> getBracketAsync,
        LookupDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        string sampleKey = $"{eventInstanceId}:{leagueToken}:{index}";
        if (ratingSamples.TryGetValue(sampleKey, out RatingSampleCacheEntry? cached) &&
            cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.SkillRating;
        }

        BracketData? bracket = await getBracketAsync(index).ConfigureAwait(false);
        if (bracket is null)
        {
            return null;
        }

        foreach (Participant participant in bracket.Players.Take(2))
        {
            if (string.IsNullOrWhiteSpace(participant.PlayerId))
            {
                continue;
            }

            try
            {
                using JsonDocument profile = await GetArenaProfileAsync(
                    new { playerId = participant.PlayerId, playerDetailsOnly = true },
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                int? rating = ReadSkillRating(profile.RootElement);
                if (rating is > 0)
                {
                    ratingSamples[sampleKey] = new RatingSampleCacheEntry(
                        rating.Value,
                        DateTimeOffset.UtcNow.Add(RatingSampleCacheDuration));
                    return rating;
                }
            }
            catch (HttpRequestException)
            {
                // Try the next participant before giving up on this sample.
            }
        }

        return null;
    }

    private async Task<BracketMatch?> CheckIndexesAsync(
        IReadOnlyCollection<int> indexes,
        ISet<int> checkedIndexes,
        long allyCode,
        string playerId,
        Func<int, Task<BracketData?>> getBracketAsync,
        LookupDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        var pending = new List<int>(BracketBatchSize);
        foreach (int index in indexes)
        {
            if (!checkedIndexes.Add(index))
            {
                continue;
            }

            pending.Add(index);
            if (pending.Count < BracketBatchSize)
            {
                continue;
            }

            BracketMatch? match = await CheckBatchAsync(pending, allyCode, playerId, getBracketAsync, diagnostics).ConfigureAwait(false);
            if (match is not null)
            {
                return match;
            }

            pending.Clear();
            await Task.Delay(GetBatchDelay(diagnostics), cancellationToken).ConfigureAwait(false);
        }

        if (pending.Count > 0)
        {
            return await CheckBatchAsync(pending, allyCode, playerId, getBracketAsync, diagnostics).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<BracketMatch?> CheckBatchAsync(
        IReadOnlyCollection<int> indexes,
        long allyCode,
        string playerId,
        Func<int, Task<BracketData?>> getBracketAsync,
        LookupDiagnostics diagnostics)
    {
        BracketData?[] brackets = await Task.WhenAll(indexes.Select(getBracketAsync)).ConfigureAwait(false);
        foreach (BracketData? bracket in brackets)
        {
            if (bracket is null)
            {
                continue;
            }

            int playerIndex = Array.FindIndex(bracket.Players, participant =>
                string.Equals(participant.PlayerId, playerId, StringComparison.Ordinal) ||
                participant.AllyCode == allyCode);
            if (playerIndex >= 0)
            {
                diagnostics.BracketId = bracket.BracketId;
                return new BracketMatch(bracket.BracketId, bracket.Index, bracket.Players, playerIndex);
            }
        }

        return null;
    }

    private BracketMatch CacheMatch(
        BracketMatch match,
        long allyCode,
        string eventInstanceId,
        string leagueToken)
    {
        locationCache[LocationKey(allyCode, eventInstanceId, leagueToken)] = new LocationCacheEntry(
            match.Index,
            DateTimeOffset.UtcNow.Add(LocationCacheDuration));
        lastKnownBracketIndexes[LastKnownKey(allyCode, leagueToken)] = match.Index;
        return match;
    }

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
