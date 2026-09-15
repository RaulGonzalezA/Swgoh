namespace Swgoh.Application.Gac;

public sealed class GacOptimizationCoordinator : IDisposable
{
    private const int MaxConcurrentOptimizations = 2;
    private readonly SemaphoreSlim semaphore = new(MaxConcurrentOptimizations, MaxConcurrentOptimizations);

    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    public void Dispose() => semaphore.Dispose();

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? current = semaphore;

        public void Dispose() => Interlocked.Exchange(ref current, null)?.Release();
    }
}
