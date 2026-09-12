using System.Collections.Concurrent;

namespace Swgoh.Application.Players;

internal sealed class PlayerRefreshLock
{
    private readonly ConcurrentDictionary<long, LockEntry> entries = new();

    public async ValueTask<IDisposable> AcquireAsync(long allyCode, CancellationToken cancellationToken)
    {
        while (true)
        {
            LockEntry entry = entries.GetOrAdd(allyCode, static _ => new LockEntry());
            Interlocked.Increment(ref entry.ReferenceCount);

            if (!entries.TryGetValue(allyCode, out LockEntry? current) || !ReferenceEquals(current, entry))
            {
                ReleaseReference(allyCode, entry);
                continue;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Releaser(this, allyCode, entry);
            }
            catch
            {
                ReleaseReference(allyCode, entry);
                throw;
            }
        }
    }

    private void Release(long allyCode, LockEntry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(allyCode, entry);
    }

    private void ReleaseReference(long allyCode, LockEntry entry)
    {
        if (Interlocked.Decrement(ref entry.ReferenceCount) != 0)
        {
            return;
        }

        if (entries.TryRemove(new KeyValuePair<long, LockEntry>(allyCode, entry)))
        {
            entry.Semaphore.Dispose();
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount;
    }

    private sealed class Releaser(PlayerRefreshLock owner, long allyCode, LockEntry entry) : IDisposable
    {
        private PlayerRefreshLock? owner = owner;

        public void Dispose()
        {
            PlayerRefreshLock? currentOwner = Interlocked.Exchange(ref owner, null);
            currentOwner?.Release(allyCode, entry);
        }
    }
}
