using System.Collections.Concurrent;

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
        Func<Task<CurrentGacScoutingResult>> factory);

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
            async () =>
            {
                CurrentGacScoutingResult result = await inner.GetAsync(
                    allyCode,
                    formatOverride,
                    normalizedMaxRounds,
                    CancellationToken.None).ConfigureAwait(false);

                ProfileVersions? finalVersions = await TryReadProfileVersionsAsync(
                    allyCode,
                    opponent.OpponentAllyCode,
                    CancellationToken.None).ConfigureAwait(false);
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
            }).ConfigureAwait(false);
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
