namespace Swgoh.Domain.Conquest;

public enum ConquestDataDiskTargetType
{
    AnyTeam = 1,
    Faction = 2,
    SpecificUnits = 3
}

public sealed record ConquestDataDiskTarget(
    ConquestDataDiskTargetType Type,
    string? Faction,
    IReadOnlyCollection<string> UnitDefinitionIds,
    int MinimumMatchingUnits)
{
    public static ConquestDataDiskTarget Create(
        ConquestDataDiskTargetType type,
        string? faction,
        IEnumerable<string>? unitDefinitionIds,
        int minimumMatchingUnits)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        string? normalizedFaction = string.IsNullOrWhiteSpace(faction) ? null : faction.Trim();
        string[] units =
        [
            .. (unitDefinitionIds ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        ArgumentOutOfRangeException.ThrowIfLessThan(minimumMatchingUnits, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumMatchingUnits, 5);

        if (type == ConquestDataDiskTargetType.Faction && normalizedFaction is null)
        {
            throw new ArgumentException("Faction-targeted disks require a faction.", nameof(faction));
        }

        if (type == ConquestDataDiskTargetType.SpecificUnits && units.Length == 0)
        {
            throw new ArgumentException("Specific-unit disks require at least one unit.", nameof(unitDefinitionIds));
        }

        if (type == ConquestDataDiskTargetType.SpecificUnits && minimumMatchingUnits > units.Length)
        {
            throw new ArgumentException(
                "Minimum matching units cannot exceed the number of configured units.",
                nameof(minimumMatchingUnits));
        }

        return new ConquestDataDiskTarget(type, normalizedFaction, units, minimumMatchingUnits);
    }
}

public sealed record ConquestDataDisk(
    Guid Id,
    string Name,
    int CapacityCost,
    decimal PlannerBonus,
    ConquestDataDiskTarget Target,
    IReadOnlyCollection<Guid> SupportedFeatIds,
    string? Notes)
{
    public static ConquestDataDisk Create(
        Guid id,
        string name,
        int capacityCost,
        decimal plannerBonus,
        ConquestDataDiskTarget target,
        IEnumerable<Guid>? supportedFeatIds,
        string? notes)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Data disk ID cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityCost, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacityCost, 100);
        ArgumentOutOfRangeException.ThrowIfNegative(plannerBonus);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(plannerBonus, 25m);

        Guid[] featIds =
        [
            .. (supportedFeatIds ?? [])
                .Where(value => value != Guid.Empty)
                .Distinct()
        ];
        string? normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        return new ConquestDataDisk(
            id,
            name.Trim(),
            capacityCost,
            plannerBonus,
            target,
            featIds,
            normalizedNotes);
    }
}

public sealed record ConquestDiskLoadout(
    Guid Id,
    string Name,
    IReadOnlyCollection<Guid> DiskIds)
{
    public static ConquestDiskLoadout Create(
        Guid id,
        string name,
        IEnumerable<Guid>? diskIds)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Data disk loadout ID cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Guid[] ids =
        [
            .. (diskIds ?? [])
                .Where(value => value != Guid.Empty)
                .Distinct()
        ];
        if (ids.Length == 0)
        {
            throw new ArgumentException("A data disk loadout must contain at least one disk.", nameof(diskIds));
        }

        return new ConquestDiskLoadout(id, name.Trim(), ids);
    }
}
