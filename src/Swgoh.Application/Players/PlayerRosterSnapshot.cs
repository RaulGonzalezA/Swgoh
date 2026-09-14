using System.Collections.Concurrent;

namespace Swgoh.Application.Players;

public sealed record PlayerRosterSnapshot(
    long AllyCode,
    DateTimeOffset UpdatedAtUtc,
    string? PlayerName,
    long GalacticPower,
    int RosterCount,
    IReadOnlyCollection<PlayerRosterUnit> Units,
    IReadOnlyCollection<string> AvailableFactions);

internal sealed class PlayerRosterSnapshotCache
{
    private readonly ConcurrentDictionary<long, PlayerRosterSnapshot> snapshots = new();

    public bool TryGet(long allyCode, DateTimeOffset updatedAtUtc, out PlayerRosterSnapshot? snapshot)
    {
        if (snapshots.TryGetValue(allyCode, out PlayerRosterSnapshot? cached)
            && cached.UpdatedAtUtc == updatedAtUtc)
        {
            snapshot = cached;
            return true;
        }

        snapshot = null;
        return false;
    }

    public void Set(PlayerRosterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshots[snapshot.AllyCode] = snapshot;
    }
}
