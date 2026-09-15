using System.Diagnostics;

using Swgoh.Infrastructure.Comlink;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.Comlink;

public sealed class GacComlinkRequestLimiterTests
{
    [Fact]
    public async Task AcquireAsync_RespectsGlobalConcurrencyLimit()
    {
        using var limiter = new GacComlinkRequestLimiter(
            maxConcurrency: 2,
            permitLimit: 100,
            window: TimeSpan.FromSeconds(1));
        CancellationToken token = TestContext.Current.CancellationToken;

        using IDisposable first = await limiter.AcquireAsync(token);
        using IDisposable second = await limiter.AcquireAsync(token);
        Task<IDisposable> thirdTask = limiter.AcquireAsync(token).AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(50), token);
        Assert.False(thirdTask.IsCompleted);

        first.Dispose();
        using IDisposable third = await thirdTask.WaitAsync(TimeSpan.FromSeconds(2), token);
        Assert.True(thirdTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AcquireAsync_PacesRequestStartsAcrossWindow()
    {
        TimeSpan window = TimeSpan.FromMilliseconds(150);
        using var limiter = new GacComlinkRequestLimiter(
            maxConcurrency: 8,
            permitLimit: 2,
            window: window);
        CancellationToken token = TestContext.Current.CancellationToken;

        using (await limiter.AcquireAsync(token))
        {
        }

        using (await limiter.AcquireAsync(token))
        {
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        using IDisposable third = await limiter.AcquireAsync(token);
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(75),
            $"Third request started too early after {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public async Task AcquireAsync_CancelledWait_DoesNotLeakConcurrencyPermit()
    {
        using var limiter = new GacComlinkRequestLimiter(
            maxConcurrency: 1,
            permitLimit: 100,
            window: TimeSpan.FromSeconds(1));
        CancellationToken token = TestContext.Current.CancellationToken;

        IDisposable first = await limiter.AcquireAsync(token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            limiter.AcquireAsync(cancellation.Token).AsTask());

        first.Dispose();
        using IDisposable next = await limiter.AcquireAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(2), token);
    }
}
