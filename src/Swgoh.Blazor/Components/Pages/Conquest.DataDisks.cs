using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class Conquest
{
    private const int DefaultDiskCapacityLimit = 12;
    private readonly List<DataDiskDraft> dataDisks = [];
    private readonly List<DiskLoadoutDraft> diskLoadouts = [];
    private readonly HashSet<Guid> selectedLoadoutDiskIds = [];

    protected bool ShowDiskEditor { get; private set; }
    protected bool ShowLoadoutEditor { get; private set; }
    protected int DiskCapacityLimit { get; set; } = DefaultDiskCapacityLimit;

    protected string NewDiskName { get; set; } = string.Empty;
    protected int NewDiskCapacityCost { get; set; } = 1;
    protected decimal NewDiskPlannerBonus { get; set; } = 2m;
    protected string NewDiskTargetType { get; set; } = "AnyTeam";
    protected string NewDiskFaction { get; set; } = string.Empty;
    protected string NewDiskSpecificUnitId { get; set; } = string.Empty;
    protected int NewDiskMinimumMatchingUnits { get; set; } = 1;
    protected Guid? NewDiskSupportedFeatId { get; set; }
    protected string NewDiskNotes { get; set; } = string.Empty;
    protected string NewLoadoutName { get; set; } = string.Empty;

    protected IReadOnlyCollection<DataDiskDraft> DataDisks => dataDisks;
    protected IReadOnlyCollection<DiskLoadoutDraft> DiskLoadouts => diskLoadouts;
    protected int SelectedLoadoutCapacity => selectedLoadoutDiskIds
        .Select(id => dataDisks.FirstOrDefault(disk => disk.Id == id)?.CapacityCost ?? 0)
        .Sum();

    protected bool CanAddDisk =>
        !string.IsNullOrWhiteSpace(NewDiskName) &&
        NewDiskCapacityCost is >= 1 and <= 100 &&
        NewDiskPlannerBonus is >= 0m and <= 25m &&
        NewDiskMinimumMatchingUnits is >= 1 and <= 5 &&
        (NewDiskTargetType switch
        {
            "Faction" => !string.IsNullOrWhiteSpace(NewDiskFaction),
            "SpecificUnits" => !string.IsNullOrWhiteSpace(NewDiskSpecificUnitId),
            _ => true
        });

    protected bool CanAddLoadout =>
        !string.IsNullOrWhiteSpace(NewLoadoutName) &&
        selectedLoadoutDiskIds.Count > 0 &&
        SelectedLoadoutCapacity <= Math.Clamp(DiskCapacityLimit, 1, 100);

    protected void ToggleDiskEditor() => ShowDiskEditor = !ShowDiskEditor;

    protected void ToggleLoadoutEditor() => ShowLoadoutEditor = !ShowLoadoutEditor;

    protected void AddDataDisk()
    {
        if (!CanAddDisk)
        {
            return;
        }

        IReadOnlyCollection<string> units = NewDiskTargetType == "SpecificUnits"
            ? [NewDiskSpecificUnitId]
            : [];
        var disk = new DataDiskDraft
        {
            Id = Guid.NewGuid(),
            Name = NewDiskName.Trim(),
            CapacityCost = NewDiskCapacityCost,
            PlannerBonus = NewDiskPlannerBonus,
            TargetType = NewDiskTargetType,
            Faction = NewDiskTargetType == "Faction" ? NewDiskFaction : null,
            UnitDefinitionIds = [.. units],
            MinimumMatchingUnits = NewDiskMinimumMatchingUnits,
            SupportedFeatIds = NewDiskSupportedFeatId is Guid featId ? [featId] : [],
            Notes = string.IsNullOrWhiteSpace(NewDiskNotes) ? null : NewDiskNotes.Trim()
        };
        dataDisks.Add(disk);
        ResetDiskBuilder();
        Optimization = null;
    }

    protected void RemoveDataDisk(Guid id)
    {
        dataDisks.RemoveAll(disk => disk.Id == id);
        selectedLoadoutDiskIds.Remove(id);
        foreach (DiskLoadoutDraft loadout in diskLoadouts)
        {
            loadout.DiskIds.Remove(id);
        }

        diskLoadouts.RemoveAll(loadout => loadout.DiskIds.Count == 0);
        Optimization = null;
    }

    protected void ToggleLoadoutDisk(Guid diskId)
    {
        if (!selectedLoadoutDiskIds.Add(diskId))
        {
            selectedLoadoutDiskIds.Remove(diskId);
        }
    }

    protected bool IsLoadoutDiskSelected(Guid diskId) => selectedLoadoutDiskIds.Contains(diskId);

    protected void AddDiskLoadout()
    {
        if (!CanAddLoadout)
        {
            return;
        }

        diskLoadouts.Add(new DiskLoadoutDraft
        {
            Id = Guid.NewGuid(),
            Name = NewLoadoutName.Trim(),
            DiskIds = [.. selectedLoadoutDiskIds]
        });
        NewLoadoutName = string.Empty;
        selectedLoadoutDiskIds.Clear();
        ShowLoadoutEditor = false;
        Optimization = null;
    }

    protected void RemoveDiskLoadout(Guid id)
    {
        diskLoadouts.RemoveAll(loadout => loadout.Id == id);
        Optimization = null;
    }

    protected int GetLoadoutCapacity(DiskLoadoutDraft loadout) => loadout.DiskIds
        .Select(id => dataDisks.FirstOrDefault(disk => disk.Id == id)?.CapacityCost ?? 0)
        .Sum();

    protected string DiskTargetLabel(DataDiskDraft disk) => disk.TargetType switch
    {
        "Faction" => $"{disk.MinimumMatchingUnits}+ de {disk.Faction}",
        "SpecificUnits" => disk.UnitDefinitionIds.Count == 1
            ? $"Incluye {CharacterName(disk.UnitDefinitionIds.First())}"
            : $"{disk.MinimumMatchingUnits}+ unidades concretas",
        _ => "Cualquier equipo"
    };

    protected string SupportedFeatLabel(DataDiskDraft disk)
    {
        if (disk.SupportedFeatIds.Count == 0)
        {
            return "Sin hazaña específica";
        }

        return string.Join(", ", disk.SupportedFeatIds.Select(id =>
            feats.FirstOrDefault(feat => feat.Id == id)?.Name ?? "Hazaña eliminada"));
    }

    protected string LoadoutDiskNames(DiskLoadoutDraft loadout) => string.Join(", ", loadout.DiskIds.Select(id =>
        dataDisks.FirstOrDefault(disk => disk.Id == id)?.Name ?? "Disco eliminado"));

    private string CharacterName(string definitionId) => roster.FirstOrDefault(unit => string.Equals(
        unit.DefinitionId,
        definitionId,
        StringComparison.OrdinalIgnoreCase))?.Name ?? definitionId;

    private void ResetDiskState()
    {
        dataDisks.Clear();
        diskLoadouts.Clear();
        selectedLoadoutDiskIds.Clear();
        DiskCapacityLimit = DefaultDiskCapacityLimit;
        ResetDiskBuilder();
        NewLoadoutName = string.Empty;
    }

    private void ResetDiskBuilder()
    {
        NewDiskName = string.Empty;
        NewDiskCapacityCost = 1;
        NewDiskPlannerBonus = 2m;
        NewDiskTargetType = "AnyTeam";
        NewDiskFaction = string.Empty;
        NewDiskSpecificUnitId = string.Empty;
        NewDiskMinimumMatchingUnits = 1;
        NewDiskSupportedFeatId = null;
        NewDiskNotes = string.Empty;
        ShowDiskEditor = false;
    }

    private void OnFeatRemoved(Guid id)
    {
        foreach (DataDiskDraft disk in dataDisks)
        {
            disk.SupportedFeatIds.Remove(id);
        }
    }

    private IReadOnlyCollection<ConquestApiClient.SaveDataDiskRequest> BuildDataDiskSaveRequests() =>
        [
            .. dataDisks.Select(disk => new ConquestApiClient.SaveDataDiskRequest(
                disk.Id,
                disk.Name,
                disk.CapacityCost,
                disk.PlannerBonus,
                disk.TargetType,
                disk.Faction,
                disk.UnitDefinitionIds,
                disk.MinimumMatchingUnits,
                disk.SupportedFeatIds,
                disk.Notes))
        ];

    private IReadOnlyCollection<ConquestApiClient.SaveDiskLoadoutRequest> BuildDiskLoadoutSaveRequests() =>
        [
            .. diskLoadouts.Select(loadout => new ConquestApiClient.SaveDiskLoadoutRequest(
                loadout.Id,
                loadout.Name,
                loadout.DiskIds))
        ];

    private void MapDataDiskPlan(ConquestApiClient.PlanViewModel plan)
    {
        DiskCapacityLimit = plan.DiskCapacityLimit;
        dataDisks.Clear();
        dataDisks.AddRange(plan.DataDisks.Select(disk => new DataDiskDraft
        {
            Id = disk.Id,
            Name = disk.Name,
            CapacityCost = disk.CapacityCost,
            PlannerBonus = disk.PlannerBonus,
            TargetType = disk.TargetType,
            Faction = disk.Faction,
            UnitDefinitionIds = [.. disk.UnitDefinitionIds],
            MinimumMatchingUnits = disk.MinimumMatchingUnits,
            SupportedFeatIds = [.. disk.SupportedFeatIds],
            Notes = disk.Notes
        }));

        diskLoadouts.Clear();
        diskLoadouts.AddRange(plan.DiskLoadouts.Select(loadout => new DiskLoadoutDraft
        {
            Id = loadout.Id,
            Name = loadout.Name,
            DiskIds = [.. loadout.DiskIds]
        }));
        selectedLoadoutDiskIds.Clear();
    }

    protected sealed class DataDiskDraft
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int CapacityCost { get; init; }
        public decimal PlannerBonus { get; init; }
        public string TargetType { get; init; } = "AnyTeam";
        public string? Faction { get; init; }
        public IReadOnlyCollection<string> UnitDefinitionIds { get; init; } = [];
        public int MinimumMatchingUnits { get; init; }
        public List<Guid> SupportedFeatIds { get; init; } = [];
        public string? Notes { get; init; }
    }

    protected sealed class DiskLoadoutDraft
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public List<Guid> DiskIds { get; init; } = [];
    }
}
