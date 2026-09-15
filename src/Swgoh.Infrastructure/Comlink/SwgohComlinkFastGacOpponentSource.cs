using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Polly.Timeout;

using Swgoh.Application.Caching;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed partial class SwgohComlinkFastGacOpponentSource(
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
        ArgumentOutOfRangeException.ThrowIfLessThan(allyCode, 100_000_000L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(allyCode, 999_999_999L);

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
}
