using Swgoh.Application.Caching;

using Xunit;

namespace Swgoh.Application.UnitTests.Caching;

public sealed class BoundedMemoryCacheTests
{
    [Fact]
    public void Set_WhenSizeLimitIsReached_KeepsCacheBounded()
    {
        using var cache = new BoundedMemoryCache<int, string>(sizeLimit: 2);

        cache[1] = "one";
        cache[2] = "two";
        cache[3] = "three";

        Assert.InRange(cache.Count, 0, 2);
    }

    [Fact]
    public void Set_WithExpiredAbsoluteExpiration_DoesNotRetainEntry()
    {
        using var cache = new BoundedMemoryCache<int, CacheValue>(
            sizeLimit: 4,
            absoluteExpirationSelector: static value => value.ExpiresAtUtc);

        cache[1] = new CacheValue("expired", DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.False(cache.TryGetValue(1, out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Set_WithFutureAbsoluteExpiration_ReturnsEntry()
    {
        using var cache = new BoundedMemoryCache<int, CacheValue>(
            sizeLimit: 4,
            absoluteExpirationSelector: static value => value.ExpiresAtUtc);
        var expected = new CacheValue("live", DateTimeOffset.UtcNow.AddMinutes(1));

        cache[1] = expected;

        Assert.True(cache.TryGetValue(1, out CacheValue? actual));
        Assert.Equal(expected, actual);
    }

    private sealed record CacheValue(string Value, DateTimeOffset ExpiresAtUtc);
}
