namespace Swgoh.Application.Gac;

public sealed class GacPlannerWriteContext
{
    private readonly AsyncLocal<long?> expectedVersion = new();

    public long? ExpectedVersion => expectedVersion.Value;

    public IDisposable Begin(long version)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        long? previous = expectedVersion.Value;
        expectedVersion.Value = version;
        return new Scope(this, previous);
    }

    private sealed class Scope(GacPlannerWriteContext owner, long? previous) : IDisposable
    {
        private GacPlannerWriteContext? owner = owner;

        public void Dispose()
        {
            GacPlannerWriteContext? current = Interlocked.Exchange(ref owner, null);
            if (current is not null)
            {
                current.expectedVersion.Value = previous;
            }
        }
    }
}
