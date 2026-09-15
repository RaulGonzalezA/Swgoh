using Swgoh.Application.Caching;

namespace Swgoh.Application.Players;

public sealed record PlayerRosterSnapshot(
    long AllyCode,
    DateTimeOffset UpdatedAtUtc,
    string? PlayerName,
    long GalacticPower,
    int RosterCount,
    IReadOnlyCollection<PlayerRosterUnit> Units,
    IReadOnlyCollection<string> AvailableFactions);

internal sealed class PlayerRosterSnapshotCache : IDisposable
{
    private const long CacheSizeLimit = 128;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private readonly BoundedMemoryCache<long, PlayerRosterSnapshot> snapshots = new(
        CacheSizeLimit,
        defaultLifetime: CacheDuration);

    public bool TryGet(long allyCode, DateTimeOffset updatedAtUtc, out PlayerRosterSnapshot? snapshot)
    {
        if (snapshots.TryGetValue(allyCode, out PlayerRosterSnapshot? cached) && cached is not null)
        {
            if (cached.UpdatedAtUtc == updatedAtUtc)
            {
                snapshot = cached;
                return true;
            }

            snapshots.Remove(allyCode);
        }

        snapshot = null;
        return false;
    }

    public void Set(PlayerRosterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshots[snapshot.AllyCode] = snapshot;
    }

    public void Dispose() => snapshots.Dispose();
}
