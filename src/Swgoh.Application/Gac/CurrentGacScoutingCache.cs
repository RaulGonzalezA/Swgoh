using System.Collections.Concurrent;

using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface ICurrentGacScoutingCache
{
    long GetGeneration(long allyCode);

    bool TryGet(
        CurrentGacScoutingCacheKey key,
        out CurrentGacScoutingResult? result);

    Task<CurrentGacScoutingResult> GetOrCreateAsync(
        CurrentGacScoutingCacheKey key,
        Func<Task<CurrentGacScoutingResult>> factory);

    void Invalidate(long allyCode);
}

public readonly record struct CurrentGacScoutingCacheKey(
    long AllyCode,
    string EventInstanceId,
    int? RoundNumber,
    long OpponentAllyCode,
    GacFormat Format,
    int MaxRounds,
    long Generation);

internal sealed class CurrentGacScoutingCache : ICurrentGacScoutingCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(45);

    private readonly ConcurrentDictionary<CurrentGacScoutingCacheKey, CacheEntry> cache = new();
    private readonly ConcurrentDictionary<CurrentGacScoutingCacheKey, Lazy<Task<CurrentGacScoutingResult>>> inFlight = new();
    private readonly ConcurrentDictionary<long, long> generations = new();

    public long GetGeneration(long allyCode) => generations.GetOrAdd(allyCode, 0);

    public bool TryGet(
        CurrentGacScoutingCacheKey key,
        out CurrentGacScoutingResult? result)
    {
        if (cache.TryGetValue(key, out CacheEntry? entry))
        {
            if (entry.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                result = entry.Result;
                return true;
            }

            cache.TryRemove(key, out _);
        }

        result = null;
        return false;
    }

    public async Task<CurrentGacScoutingResult> GetOrCreateAsync(
        CurrentGacScoutingCacheKey key,
        Func<Task<CurrentGacScoutingResult>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (TryGet(key, out CurrentGacScoutingResult? cached) && cached is not null)
        {
            return cached;
        }

        Lazy<Task<CurrentGacScoutingResult>> lazy = inFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<CurrentGacScoutingResult>>(
                factory,
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            CurrentGacScoutingResult result = await lazy.Value.ConfigureAwait(false);
            if (result.Lookup.Status == CurrentGacOpponentStatus.Found && result.Lookup.Opponent is not null)
            {
                cache[key] = new CacheEntry(result, DateTimeOffset.UtcNow.Add(CacheDuration));
            }

            return result;
        }
        finally
        {
            inFlight.TryRemove(key, out _);
        }
    }

    public void Invalidate(long allyCode)
    {
        generations.AddOrUpdate(allyCode, 1, static (_, current) => current + 1);

        foreach (CurrentGacScoutingCacheKey key in cache.Keys.Where(key => key.AllyCode == allyCode))
        {
            cache.TryRemove(key, out _);
        }
    }

    private sealed record CacheEntry(CurrentGacScoutingResult Result, DateTimeOffset ExpiresAtUtc);
}

internal sealed class CachedCurrentGacScoutingService(
    ICurrentGacOpponentSource opponentSource,
    CurrentGacScoutingService inner,
    ICurrentGacScoutingCache cache) : ICurrentGacScoutingService
{
    public async Task<CurrentGacScoutingResult> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        int maxRounds,
        CancellationToken cancellationToken = default)
    {
        CurrentGacOpponentLookup lookup = await opponentSource
            .GetAsync(allyCode, formatOverride, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Status != CurrentGacOpponentStatus.Found || lookup.Opponent is null)
        {
            return new CurrentGacScoutingResult(lookup, null, null, null);
        }

        CurrentGacOpponent opponent = lookup.Opponent;
        int normalizedMaxRounds = Math.Clamp(maxRounds, 1, 200);
        var key = new CurrentGacScoutingCacheKey(
            allyCode,
            opponent.EventInstanceId,
            opponent.RoundNumber,
            opponent.OpponentAllyCode,
            opponent.Format,
            normalizedMaxRounds,
            cache.GetGeneration(allyCode));

        if (cache.TryGet(key, out CurrentGacScoutingResult? cached) && cached is not null)
        {
            return cached;
        }

        return await cache.GetOrCreateAsync(
            key,
            () => inner.GetAsync(
                allyCode,
                formatOverride,
                normalizedMaxRounds,
                CancellationToken.None)).ConfigureAwait(false);
    }
}
