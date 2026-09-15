using System.Collections.Concurrent;

using Swgoh.Application.Caching;
using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Gac;

public interface ICurrentGacScoutingCache
{
    long GetGeneration(long allyCode);

    bool TryGet(
        CurrentGacScoutingCacheKey key,
        out CurrentGacScoutingResult? result);

    Task<CurrentGacScoutingResult> GetOrCreateAsync(
        CurrentGacScoutingCacheKey key,
        Func<CancellationToken, Task<CurrentGacScoutingResult>> factory,
        CancellationToken cancellationToken = default);

    void Set(CurrentGacScoutingCacheKey key, CurrentGacScoutingResult result);

    void Invalidate(long allyCode);
}

public readonly record struct CurrentGacScoutingCacheKey(
    long AllyCode,
    string EventInstanceId,
    int? RoundNumber,
    long OpponentAllyCode,
    GacFormat Format,
    int MaxRounds,
    DateTimeOffset? PlayerUpdatedAtUtc,
    DateTimeOffset? OpponentUpdatedAtUtc,
    long Generation);

internal sealed class CurrentGacScoutingCache : ICurrentGacScoutingCache, IDisposable
{
    private const long ResultCacheSizeLimit = 512;
    private const long GenerationCacheSizeLimit = 2_048;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan GenerationCacheDuration = TimeSpan.FromHours(2);
    private static readonly TimeSpan DefaultSharedOperationTimeout = TimeSpan.FromSeconds(75);

    private readonly BoundedMemoryCache<CurrentGacScoutingCacheKey, CacheEntry> cache = new(
        ResultCacheSizeLimit,
        absoluteExpirationSelector: static entry => entry.ExpiresAtUtc);
    private readonly ConcurrentDictionary<CurrentGacScoutingCacheKey, Lazy<Task<CurrentGacScoutingResult>>> inFlight = new();
    private readonly BoundedMemoryCache<long, long> generations = new(
        GenerationCacheSizeLimit,
        defaultLifetime: GenerationCacheDuration);
    private readonly object generationGate = new();
    private readonly TimeSpan sharedOperationTimeout;
    private long generationSequence;

    public CurrentGacScoutingCache()
        : this(DefaultSharedOperationTimeout)
    {
    }

    internal CurrentGacScoutingCache(TimeSpan sharedOperationTimeout)
    {
        if (sharedOperationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sharedOperationTimeout),
                sharedOperationTimeout,
                "Shared GAC scouting timeout must be greater than zero.");
        }

        this.sharedOperationTimeout = sharedOperationTimeout;
    }

    public long GetGeneration(long allyCode)
    {
        lock (generationGate)
        {
            if (generations.TryGetValue(allyCode, out long generation))
            {
                return generation;
            }

            generation = ++generationSequence;
            generations[allyCode] = generation;
            return generation;
        }
    }

    public bool TryGet(
        CurrentGacScoutingCacheKey key,
        out CurrentGacScoutingResult? result)
    {
        if (cache.TryGetValue(key, out CacheEntry? entry))
        {
            result = entry.Result;
            return true;
        }

        result = null;
        return false;
    }

    public async Task<CurrentGacScoutingResult> GetOrCreateAsync(
        CurrentGacScoutingCacheKey key,
        Func<CancellationToken, Task<CurrentGacScoutingResult>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (TryGet(key, out CurrentGacScoutingResult? cached) && cached is not null)
        {
            return cached;
        }

        Lazy<Task<CurrentGacScoutingResult>> lazy = inFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<CurrentGacScoutingResult>>(
                () => ExecuteSharedAsync(key, factory),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Set(CurrentGacScoutingCacheKey key, CurrentGacScoutingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Lookup.Status != CurrentGacOpponentStatus.Found ||
            result.Lookup.Opponent is null ||
            key.Generation != GetGeneration(key.AllyCode))
        {
            return;
        }

        foreach (CurrentGacScoutingCacheKey staleKey in cache.Keys.Where(existing =>
                     existing.AllyCode == key.AllyCode &&
                     existing.EventInstanceId == key.EventInstanceId &&
                     existing.RoundNumber == key.RoundNumber &&
                     existing.OpponentAllyCode == key.OpponentAllyCode &&
                     existing.Format == key.Format &&
                     existing.MaxRounds == key.MaxRounds &&
                     existing != key))
        {
            cache.TryRemove(staleKey, out _);
        }

        cache[key] = new CacheEntry(result, DateTimeOffset.UtcNow.Add(CacheDuration));
    }

    public void Invalidate(long allyCode)
    {
        lock (generationGate)
        {
            generations[allyCode] = ++generationSequence;
        }

        foreach (CurrentGacScoutingCacheKey key in cache.Keys.Where(key => key.AllyCode == allyCode))
        {
            cache.TryRemove(key, out _);
        }
    }

    public void Dispose()
    {
        cache.Dispose();
        generations.Dispose();
    }

    private async Task<CurrentGacScoutingResult> ExecuteSharedAsync(
        CurrentGacScoutingCacheKey key,
        Func<CancellationToken, Task<CurrentGacScoutingResult>> factory)
    {
        using var timeoutSource = new CancellationTokenSource(sharedOperationTimeout);
        try
        {
            CurrentGacScoutingResult result = await factory(timeoutSource.Token).ConfigureAwait(false);
            if (key.Generation == GetGeneration(key.AllyCode))
            {
                Set(key, result);
            }

            return result;
        }
        finally
        {
            inFlight.TryRemove(key, out _);
        }
    }

    private sealed record CacheEntry(CurrentGacScoutingResult Result, DateTimeOffset ExpiresAtUtc);
}

internal sealed class CachedCurrentGacScoutingService(
    ICurrentGacOpponentSource opponentSource,
    CurrentGacScoutingService inner,
    ICurrentGacScoutingCache cache,
    IPlayerProfileService playerProfileService) : ICurrentGacScoutingService
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
        ProfileVersions? initialVersions = await TryReadProfileVersionsAsync(
            allyCode,
            opponent.OpponentAllyCode,
            cancellationToken).ConfigureAwait(false);
        if (initialVersions is null)
        {
            return await inner.GetAsync(
                allyCode,
                formatOverride,
                normalizedMaxRounds,
                cancellationToken).ConfigureAwait(false);
        }

        CurrentGacScoutingCacheKey initialKey = CreateKey(
            allyCode,
            opponent,
            normalizedMaxRounds,
            initialVersions.Value,
            cache.GetGeneration(allyCode));

        if (cache.TryGet(initialKey, out CurrentGacScoutingResult? cached) && cached is not null)
        {
            return cached;
        }

        return await cache.GetOrCreateAsync(
            initialKey,
            async sharedCancellationToken =>
            {
                CurrentGacScoutingResult result = await inner.GetAsync(
                    allyCode,
                    formatOverride,
                    normalizedMaxRounds,
                    sharedCancellationToken).ConfigureAwait(false);

                ProfileVersions? finalVersions = await TryReadProfileVersionsAsync(
                    allyCode,
                    opponent.OpponentAllyCode,
                    sharedCancellationToken).ConfigureAwait(false);
                if (finalVersions is null)
                {
                    cache.Invalidate(allyCode);
                    return result;
                }

                if (finalVersions.Value != initialVersions.Value)
                {
                    cache.Invalidate(allyCode);
                    CurrentGacScoutingCacheKey finalKey = CreateKey(
                        allyCode,
                        opponent,
                        normalizedMaxRounds,
                        finalVersions.Value,
                        cache.GetGeneration(allyCode));
                    cache.Set(finalKey, result);
                }

                return result;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProfileVersions?> TryReadProfileVersionsAsync(
        long playerAllyCode,
        long opponentAllyCode,
        CancellationToken cancellationToken)
    {
        try
        {
            Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(playerAllyCode, cancellationToken);
            Task<PlayerProfile?> opponentTask = playerProfileService.GetAsync(opponentAllyCode, cancellationToken);
            await Task.WhenAll(playerTask, opponentTask).ConfigureAwait(false);

            PlayerProfile? player = await playerTask.ConfigureAwait(false);
            PlayerProfile? opponent = await opponentTask.ConfigureAwait(false);
            return new ProfileVersions(player?.UpdatedAtUtc, opponent?.UpdatedAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static CurrentGacScoutingCacheKey CreateKey(
        long allyCode,
        CurrentGacOpponent opponent,
        int maxRounds,
        ProfileVersions versions,
        long generation) => new(
        allyCode,
        opponent.EventInstanceId,
        opponent.RoundNumber,
        opponent.OpponentAllyCode,
        opponent.Format,
        maxRounds,
        versions.PlayerUpdatedAtUtc,
        versions.OpponentUpdatedAtUtc,
        generation);

    private readonly record struct ProfileVersions(
        DateTimeOffset? PlayerUpdatedAtUtc,
        DateTimeOffset? OpponentUpdatedAtUtc);
}
