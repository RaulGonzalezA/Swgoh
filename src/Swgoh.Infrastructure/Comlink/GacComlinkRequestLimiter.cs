using System.Diagnostics;

namespace Swgoh.Infrastructure.Comlink;

internal sealed class GacComlinkRequestLimiter : IDisposable
{
    internal const int DefaultMaxConcurrency = 8;
    internal const int DefaultPermitLimit = 8;
    internal static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(400);

    private readonly SemaphoreSlim concurrency;
    private readonly Queue<long> requestStarts = new();
    private readonly object rateGate = new();
    private readonly int permitLimit;
    private readonly TimeSpan window;
    private int disposed;

    public GacComlinkRequestLimiter()
        : this(DefaultMaxConcurrency, DefaultPermitLimit, DefaultWindow)
    {
    }

    internal GacComlinkRequestLimiter(int maxConcurrency, int permitLimit, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        this.permitLimit = permitLimit;
        this.window = window;
    }

    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WaitForRateSlotAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(concurrency);
        }
        catch
        {
            concurrency.Release();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            concurrency.Dispose();
        }
    }

    private async Task WaitForRateSlotAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            lock (rateGate)
            {
                long now = Stopwatch.GetTimestamp();
                while (requestStarts.Count > 0 && Stopwatch.GetElapsedTime(requestStarts.Peek(), now) >= window)
                {
                    requestStarts.Dequeue();
                }

                if (requestStarts.Count < permitLimit)
                {
                    requestStarts.Enqueue(now);
                    return;
                }

                TimeSpan elapsed = Stopwatch.GetElapsedTime(requestStarts.Peek(), now);
                delay = window - elapsed;
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}

internal sealed class GacComlinkRateLimitHandler(GacComlinkRequestLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using IDisposable lease = await limiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
