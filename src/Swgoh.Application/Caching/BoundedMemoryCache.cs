using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Caching.Memory;

namespace Swgoh.Application.Caching;

public sealed class BoundedMemoryCache<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private static readonly TimeSpan ExpirationScanFrequency = TimeSpan.FromMinutes(1);

    private readonly MemoryCache cache;
    private readonly TimeSpan? defaultLifetime;
    private readonly Func<TValue, DateTimeOffset?>? absoluteExpirationSelector;
    private readonly Func<TValue, long>? sizeSelector;

    public BoundedMemoryCache(
        long sizeLimit,
        TimeSpan? defaultLifetime = null,
        Func<TValue, DateTimeOffset?>? absoluteExpirationSelector = null,
        Func<TValue, long>? sizeSelector = null)
    {
        if (sizeLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeLimit), sizeLimit, "Cache size limit must be greater than zero.");
        }

        if (defaultLifetime is TimeSpan lifetime && lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultLifetime),
                defaultLifetime,
                "Default cache lifetime must be greater than zero.");
        }

        cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = sizeLimit,
            ExpirationScanFrequency = ExpirationScanFrequency
        });
        this.defaultLifetime = defaultLifetime;
        this.absoluteExpirationSelector = absoluteExpirationSelector;
        this.sizeSelector = sizeSelector;
    }

    public int Count => cache.Count;

    public IEnumerable<TKey> Keys => cache.Keys.OfType<TKey>();

    public TValue this[TKey key]
    {
        set => Set(key, value);
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (cache.TryGetValue(key, out object? cached) && cached is TValue typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        long size = sizeSelector?.Invoke(value) ?? 1;
        if (size <= 0)
        {
            throw new InvalidOperationException("Cache entry size must be greater than zero.");
        }

        var options = new MemoryCacheEntryOptions
        {
            Size = size
        };

        DateTimeOffset? absoluteExpiration = absoluteExpirationSelector?.Invoke(value);
        if (absoluteExpiration is DateTimeOffset expiresAt)
        {
            if (expiresAt <= DateTimeOffset.UtcNow)
            {
                cache.Remove(key);
                return;
            }

            options.AbsoluteExpiration = expiresAt;
        }
        else if (defaultLifetime is TimeSpan lifetime)
        {
            options.AbsoluteExpirationRelativeToNow = lifetime;
        }

        cache.Set(key, value, options);
    }

    public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        bool found = TryGetValue(key, out value);
        cache.Remove(key);
        return found;
    }

    public void Remove(TKey key) => cache.Remove(key);

    public void Dispose() => cache.Dispose();
}
