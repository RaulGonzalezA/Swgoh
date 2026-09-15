using Microsoft.Extensions.Logging;

using Swgoh.Application.Caching;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class PersistedGacOpponentSource(
    BackgroundGacOpponentSource fallback,
    IGacBracketLocationRepository locations,
    GacExactBracketResolver exactResolver,
    IGacTelemetry gacTelemetry,
    ILogger<PersistedGacOpponentSource> logger) : IDisposable
{
    private const long ResultCacheSizeLimit = 2_048;
    private const long PersistedAttemptCacheSizeLimit = 2_048;
    private static readonly TimeSpan ResultCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PersistedAttemptDuration = TimeSpan.FromMinutes(1);

    private readonly BoundedMemoryCache<string, ResultCacheEntry> resultCache = new(
        ResultCacheSizeLimit,
        absoluteExpirationSelector: static entry => entry.ExpiresAtUtc);
    private readonly BoundedMemoryCache<string, DateTimeOffset> persistedAttempts = new(
        PersistedAttemptCacheSizeLimit,
        absoluteExpirationSelector: static expiresAt => expiresAt);

    public async Task<CurrentGacOpponentLookup> GetAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken = default)
    {
        if (!GacBracketParser.IsValidAllyCode(allyCode))
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }

        string key = $"{allyCode}:{formatOverride?.ToString() ?? "auto"}";
        if (resultCache.TryGetValue(key, out ResultCacheEntry? cached))
        {
            gacTelemetry.RecordOpponentLookup(TimeSpan.Zero, cached.Lookup.Status, cacheHit: true, "persisted-wrapper-memory");
            return cached.Lookup;
        }

        if (!persistedAttempts.TryGetValue(key, out _))
        {
            persistedAttempts[key] = DateTimeOffset.UtcNow.Add(PersistedAttemptDuration);
            try
            {
                CurrentGacOpponentLookup? persisted = await TryResolvePersistedAsync(
                    allyCode,
                    formatOverride,
                    cancellationToken).ConfigureAwait(false);
                if (persisted is not null)
                {
                    resultCache[key] = new ResultCacheEntry(persisted, DateTimeOffset.UtcNow.Add(ResultCacheDuration));
                    logger.LogInformation(
                        "Resolved GAC opponent for {AllyCode} from persisted bracket {BracketId}",
                        allyCode,
                        persisted.Opponent?.BracketId);
                    return persisted;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "Persisted GAC bracket lookup failed for {AllyCode}; falling back to live search",
                    allyCode);
            }
        }

        CurrentGacOpponentLookup lookup = await fallback
            .GetAsync(allyCode, formatOverride, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Status != CurrentGacOpponentStatus.Found || lookup.Opponent is null)
        {
            return lookup;
        }

        resultCache[key] = new ResultCacheEntry(lookup, DateTimeOffset.UtcNow.Add(ResultCacheDuration));
        try
        {
            int skillRating = await exactResolver
                .ReadSkillRatingAsync(allyCode, cancellationToken)
                .ConfigureAwait(false);
            await PersistLocationAsync(lookup.Opponent, skillRating, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not persist GAC bracket location for {AllyCode}", allyCode);
        }

        return lookup;
    }

    public void Invalidate(long allyCode)
    {
        string prefix = $"{allyCode}:";
        foreach (string key in resultCache.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            resultCache.TryRemove(key, out _);
        }

        foreach (string key in persistedAttempts.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            persistedAttempts.TryRemove(key, out _);
        }

        fallback.Invalidate(allyCode);
        logger.LogInformation("Invalidated GAC opponent caches for {AllyCode}", allyCode);
    }

    public void Dispose()
    {
        resultCache.Dispose();
        persistedAttempts.Dispose();
    }

    private async Task<CurrentGacOpponentLookup?> TryResolvePersistedAsync(
        long allyCode,
        GacFormat? formatOverride,
        CancellationToken cancellationToken)
    {
        GacBracketLocation? location = await locations
            .FindLatestAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (location is null)
        {
            return null;
        }

        CurrentGacOpponent? opponent = await exactResolver
            .ResolvePersistedAsync(allyCode, location, formatOverride, cancellationToken)
            .ConfigureAwait(false);
        return opponent is null ? null : CurrentGacOpponentLookup.Found(opponent);
    }

    private Task PersistLocationAsync(
        CurrentGacOpponent opponent,
        int skillRating,
        CancellationToken cancellationToken)
    {
        int separator = opponent.BracketId.LastIndexOf(':');
        if (separator < 0 ||
            !int.TryParse(opponent.BracketId[(separator + 1)..], out int bracketIndex) ||
            bracketIndex < 0)
        {
            return Task.CompletedTask;
        }

        return locations.UpsertAsync(
            new GacBracketLocation(
                opponent.PlayerAllyCode,
                opponent.EventId,
                opponent.EventInstanceId,
                opponent.League,
                opponent.Format,
                bracketIndex,
                skillRating,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private sealed record ResultCacheEntry(CurrentGacOpponentLookup Lookup, DateTimeOffset ExpiresAtUtc);
}
