namespace Swgoh.Domain.Conquest;

public sealed partial class ConquestPlan
{
    public const int DefaultDiskCapacityLimit = 12;

    private readonly List<ConquestDataDisk> dataDisks = [];
    private readonly List<ConquestDiskLoadout> diskLoadouts = [];

    public int DiskCapacityLimit { get; private set; } = DefaultDiskCapacityLimit;
    public IReadOnlyList<ConquestDataDisk> DataDisks => dataDisks;
    public IReadOnlyList<ConquestDiskLoadout> DiskLoadouts => diskLoadouts;

    public void ReplaceDataDisks(
        int diskCapacityLimit,
        IEnumerable<ConquestDataDisk> newDataDisks,
        IEnumerable<ConquestDiskLoadout> newDiskLoadouts,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(updatedAtUtc, CreatedAtUtc);

        ApplyDataDisks(diskCapacityLimit, newDataDisks, newDiskLoadouts);
        UpdatedAtUtc = updatedAtUtc;
    }

    public void RestoreDataDisks(
        int diskCapacityLimit,
        IEnumerable<ConquestDataDisk>? restoredDataDisks,
        IEnumerable<ConquestDiskLoadout>? restoredDiskLoadouts)
    {
        ApplyDataDisks(
            diskCapacityLimit,
            restoredDataDisks ?? [],
            restoredDiskLoadouts ?? []);
    }

    public int GetLoadoutCapacity(ConquestDiskLoadout loadout)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        return loadout.DiskIds.Sum(id => dataDisks.First(disk => disk.Id == id).CapacityCost);
    }

    private void ApplyDataDisks(
        int diskCapacityLimit,
        IEnumerable<ConquestDataDisk> sourceDisks,
        IEnumerable<ConquestDiskLoadout> sourceLoadouts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(diskCapacityLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(diskCapacityLimit, 100);

        ArgumentNullException.ThrowIfNull(sourceDisks);
        ArgumentNullException.ThrowIfNull(sourceLoadouts);

        ConquestDataDisk[] disks = [.. sourceDisks];
        if (disks.Select(disk => disk.Id).Distinct().Count() != disks.Length)
        {
            throw new ArgumentException("Data disk IDs must be unique.", nameof(sourceDisks));
        }

        HashSet<Guid> featIds = Feats.Select(feat => feat.Id).ToHashSet();
        foreach (ConquestDataDisk disk in disks)
        {
            Guid[] missingFeatIds = [.. disk.SupportedFeatIds.Where(id => !featIds.Contains(id))];
            if (missingFeatIds.Length > 0)
            {
                throw new ArgumentException(
                    $"Data disk '{disk.Name}' references feat IDs that are not part of this Conquest plan.",
                    nameof(sourceDisks));
            }
        }

        ConquestDiskLoadout[] loadouts = [.. sourceLoadouts];
        if (loadouts.Select(loadout => loadout.Id).Distinct().Count() != loadouts.Length)
        {
            throw new ArgumentException("Data disk loadout IDs must be unique.", nameof(sourceLoadouts));
        }

        Dictionary<Guid, ConquestDataDisk> disksById = disks.ToDictionary(disk => disk.Id);
        foreach (ConquestDiskLoadout loadout in loadouts)
        {
            Guid[] missingDiskIds = [.. loadout.DiskIds.Where(id => !disksById.ContainsKey(id))];
            if (missingDiskIds.Length > 0)
            {
                throw new ArgumentException(
                    $"Loadout '{loadout.Name}' references disks that are not in the inventory.",
                    nameof(sourceLoadouts));
            }

            int usedCapacity = loadout.DiskIds.Sum(id => disksById[id].CapacityCost);
            if (usedCapacity > diskCapacityLimit)
            {
                throw new ArgumentException(
                    $"Loadout '{loadout.Name}' uses {usedCapacity} capacity but the configured limit is {diskCapacityLimit}.",
                    nameof(sourceLoadouts));
            }
        }

        DiskCapacityLimit = diskCapacityLimit;
        dataDisks.Clear();
        dataDisks.AddRange(disks);
        diskLoadouts.Clear();
        diskLoadouts.AddRange(loadouts);
    }
}
