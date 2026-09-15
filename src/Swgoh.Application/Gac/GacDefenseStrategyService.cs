using Swgoh.Application.Abstractions;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacDefenseStrategyService
{
    Task<GacDefenseStrategySnapshot> GetAsync(
        long allyCode,
        GacFormat format,
        CancellationToken cancellationToken = default);

    Task<GacDefenseStrategySnapshot> SaveAsync(
        long allyCode,
        SaveGacDefenseStrategy input,
        CancellationToken cancellationToken = default);

    Task<GacDefenseGenerationResult> GenerateCurrentAsync(
        long allyCode,
        bool apply,
        CancellationToken cancellationToken = default);
}

public interface IGacDefenseStrategyRepository
{
    Task<GacDefenseStrategyProfile?> FindAsync(
        long allyCode,
        GacFormat format,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        GacDefenseStrategyProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed record GacDefenseTemplateSlot(
    int Position,
    string Zone,
    Guid? PinnedTeamPresetId);

public sealed record GacDefenseStrategyProfile(
    long AllyCode,
    GacFormat Format,
    IReadOnlyCollection<GacDefenseTemplateSlot> Slots,
    IReadOnlyCollection<Guid> ReservedAttackPresetIds,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveGacDefenseStrategy(
    GacFormat Format,
    IReadOnlyCollection<GacDefenseTemplateSlot> Slots,
    IReadOnlyCollection<Guid> ReservedAttackPresetIds);

public sealed record GacDefensePresetSummary(
    Guid Id,
    string Name,
    GacPlannerTeamUse Use,
    bool IsFleet);

public sealed record GacDefenseStrategySnapshot(
    GacDefenseStrategyProfile Profile,
    IReadOnlyCollection<GacDefensePresetSummary> Presets);

public sealed record GacGeneratedDefenseAssignment(
    int Position,
    string Zone,
    Guid TeamPresetId,
    string TeamName,
    bool Pinned,
    bool IsFleet,
    long GalacticPower);

public sealed record GacDefenseGenerationResult(
    GacFormat Format,
    bool Applied,
    IReadOnlyCollection<GacGeneratedDefenseAssignment> Assignments,
    IReadOnlyCollection<string> Warnings,
    DateTimeOffset? PlanUpdatedAtUtc);

internal sealed class GacDefenseStrategyService(
    IGacDefenseStrategyRepository repository,
    IGacTeamPresetRepository presetRepository,
    IGacPlannerService plannerService,
    IClock clock) : IGacDefenseStrategyService
{
    public async Task<GacDefenseStrategySnapshot> GetAsync(
        long allyCode,
        GacFormat format,
        CancellationToken cancellationToken = default)
    {
        ValidateFormat(format);
        IReadOnlyCollection<GacTeamPreset> presets = await presetRepository
            .GetAsync(allyCode, format, cancellationToken)
            .ConfigureAwait(false);
        GacDefenseStrategyProfile? stored = await repository
            .FindAsync(allyCode, format, cancellationToken)
            .ConfigureAwait(false);
        GacLeague? league = await TryGetCurrentLeagueAsync(allyCode, format, cancellationToken).ConfigureAwait(false);
        GacDefenseStrategyProfile profile = league is GacLeague currentLeague
            ? ReconcileProfile(
                stored ?? CreateDefaultProfile(allyCode, format, currentLeague),
                currentLeague,
                presets)
            : stored ?? CreateDefaultProfile(allyCode, format, GacLeague.Carbonite);

        return new GacDefenseStrategySnapshot(profile, [.. presets.Select(ToSummary)]);
    }

    public async Task<GacDefenseStrategySnapshot> SaveAsync(
        long allyCode,
        SaveGacDefenseStrategy input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateFormat(input.Format);
        ValidateSlots(input.Slots);

        IReadOnlyCollection<GacTeamPreset> presets = await presetRepository
            .GetAsync(allyCode, input.Format, cancellationToken)
            .ConfigureAwait(false);
        HashSet<Guid> validPresetIds = presets.Select(preset => preset.Id).ToHashSet();
        Guid[] referencedPresetIds =
        [
            .. input.Slots
                .Where(slot => slot.PinnedTeamPresetId is not null)
                .Select(slot => slot.PinnedTeamPresetId!.Value)
                .Concat(input.ReservedAttackPresetIds)
                .Distinct()
        ];
        if (referencedPresetIds.Any(id => !validPresetIds.Contains(id)))
        {
            throw new ArgumentException(
                "All template teams and attack reservations must belong to the selected GAC format.",
                nameof(input));
        }

        Guid[] pinned =
        [
            .. input.Slots
                .Where(slot => slot.PinnedTeamPresetId is not null)
                .Select(slot => slot.PinnedTeamPresetId!.Value)
        ];
        if (pinned.Length != pinned.Distinct().Count())
        {
            throw new ArgumentException("A team can only be pinned to one defense slot.", nameof(input));
        }

        HashSet<Guid> reserved = input.ReservedAttackPresetIds.ToHashSet();
        if (pinned.Any(reserved.Contains))
        {
            throw new ArgumentException(
                "A team cannot be pinned on defense and reserved for attack at the same time.",
                nameof(input));
        }

        GacDefenseStrategyProfile profile = new(
            allyCode,
            input.Format,
            [.. input.Slots.OrderBy(slot => slot.Position)],
            [.. reserved],
            clock.UtcNow);
        GacLeague? league = await TryGetCurrentLeagueAsync(allyCode, input.Format, cancellationToken).ConfigureAwait(false);
        if (league is GacLeague currentLeague)
        {
            profile = ReconcileProfile(profile, currentLeague, presets);
        }

        await repository.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
        return new GacDefenseStrategySnapshot(profile, [.. presets.Select(ToSummary)]);
    }

    public async Task<GacDefenseGenerationResult> GenerateCurrentAsync(
        long allyCode,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        GacPlannerLookup lookup = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (!lookup.IsAvailable || lookup.State is null)
        {
            throw new InvalidOperationException(lookup.Message ?? "No active GAC round is available.");
        }

        GacPlannerState state = lookup.State;
        GacFormat format = state.Plan.Format;
        IReadOnlyCollection<GacTeamPreset> rawPresets = await presetRepository
            .GetAsync(allyCode, format, cancellationToken)
            .ConfigureAwait(false);
        GacDefenseStrategyProfile stored = await repository
            .FindAsync(allyCode, format, cancellationToken)
            .ConfigureAwait(false)
            ?? CreateDefaultProfile(allyCode, format, state.Plan.League);
        GacDefenseStrategyProfile profile = ReconcileProfile(stored, state.Plan.League, rawPresets);

        Generation generation = Generate(profile, state.Presets);
        if (!apply)
        {
            return new GacDefenseGenerationResult(
                format,
                Applied: false,
                generation.Assignments,
                generation.Warnings,
                state.Plan.UpdatedAtUtc);
        }

        IReadOnlyCollection<SaveGacOwnDefenseAssignment> ownDefenses =
        [
            .. generation.Assignments.Select(item =>
            {
                GacOwnDefenseAssignmentDetails? existing = state.Plan.OwnDefenses.FirstOrDefault(defense =>
                    defense.Team.Id == item.TeamPresetId &&
                    string.Equals(defense.Zone, item.Zone, StringComparison.OrdinalIgnoreCase));
                return new SaveGacOwnDefenseAssignment(existing?.Id, item.Zone, item.TeamPresetId);
            })
        ];
        IReadOnlyCollection<SaveGacVisibleDefense> visibleDefenses =
        [
            .. state.Plan.VisibleDefenses.Select(item => new SaveGacVisibleDefense(
                item.Id,
                item.Zone,
                item.Label,
                item.Squad.Leader.DefinitionId,
                [.. item.Squad.Members.Select(unit => unit.DefinitionId)],
                item.Squad.IsFleet))
        ];
        IReadOnlyCollection<SaveGacAttackAssignment> attacks =
        [
            .. state.Plan.Attacks.Select(item => new SaveGacAttackAssignment(
                item.Id,
                item.DefenseId,
                item.Team.Id,
                item.Attempt,
                item.Status,
                item.Notes))
        ];

        GacPlannerLookup saved = await plannerService
            .SaveCurrentAsync(
                allyCode,
                new SaveCurrentGacRoundPlan(ownDefenses, visibleDefenses, attacks, state.Plan.Version),
                cancellationToken)
            .ConfigureAwait(false);
        if (!saved.IsAvailable || saved.State is null)
        {
            throw new InvalidOperationException(saved.Message ?? "The generated defense could not be applied.");
        }

        return new GacDefenseGenerationResult(
            format,
            Applied: true,
            generation.Assignments,
            generation.Warnings,
            saved.State.Plan.UpdatedAtUtc);
    }

    internal static Generation Generate(
        GacDefenseStrategyProfile profile,
        IReadOnlyCollection<GacTeamPresetDetails> presets)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(presets);

        Dictionary<Guid, GacTeamPresetDetails> byId = presets.ToDictionary(preset => preset.Id);
        HashSet<Guid> reserved = profile.ReservedAttackPresetIds.ToHashSet();
        var usedPresets = new HashSet<Guid>();
        var usedUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignments = new List<GacGeneratedDefenseAssignment>();
        var warnings = new List<string>();

        foreach (GacDefenseTemplateSlot slot in profile.Slots.OrderBy(slot => slot.Position))
        {
            GacTeamPresetDetails? selected = null;
            bool pinned = false;
            if (slot.PinnedTeamPresetId is Guid pinnedId)
            {
                pinned = true;
                if (reserved.Contains(pinnedId))
                {
                    warnings.Add($"Slot {slot.Position}: el equipo fijado está reservado para ataque.");
                    continue;
                }

                byId.TryGetValue(pinnedId, out selected);
                if (selected is null)
                {
                    warnings.Add($"Slot {slot.Position}: el equipo fijado ya no existe para {FormatName(profile.Format)}.");
                    continue;
                }

                if (!MatchesZone(selected, slot.Zone))
                {
                    warnings.Add($"Slot {slot.Position}: {selected.Name} no coincide con el tipo de zona {slot.Zone}.");
                    continue;
                }

                if (Conflicts(selected, usedPresets, usedUnits))
                {
                    warnings.Add($"Slot {slot.Position}: {selected.Name} comparte unidades con otra defensa ya elegida.");
                    continue;
                }
            }
            else
            {
                selected = presets
                    .Where(preset => !reserved.Contains(preset.Id))
                    .Where(preset => MatchesZone(preset, slot.Zone))
                    .Where(preset => !Conflicts(preset, usedPresets, usedUnits))
                    .OrderBy(PresetUsePriority)
                    .ThenByDescending(TeamPower)
                    .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }

            if (selected is null)
            {
                warnings.Add($"Slot {slot.Position}: no hay un equipo compatible disponible para {slot.Zone}.");
                continue;
            }

            usedPresets.Add(selected.Id);
            foreach (GacPlannerUnitDetails unit in selected.Squad.AllUnits)
            {
                usedUnits.Add(unit.DefinitionId);
            }

            assignments.Add(new GacGeneratedDefenseAssignment(
                slot.Position,
                slot.Zone,
                selected.Id,
                selected.Name,
                pinned,
                selected.Squad.IsFleet,
                TeamPower(selected)));
        }

        return new Generation(assignments, warnings);
    }

    private async Task<GacLeague?> TryGetCurrentLeagueAsync(
        long allyCode,
        GacFormat format,
        CancellationToken cancellationToken)
    {
        GacPlannerLookup current = await plannerService.GetCurrentAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return current.IsAvailable && current.State?.Plan.Format == format
            ? current.State.Plan.League
            : null;
    }

    private GacDefenseStrategyProfile CreateDefaultProfile(long allyCode, GacFormat format, GacLeague league) => new(
        allyCode,
        format,
        [.. GacBoardLayouts.Get(league, format).Slots.Select(slot =>
            new GacDefenseTemplateSlot(slot.Position, slot.Zone, null))],
        [],
        clock.UtcNow);

    private static GacDefenseStrategyProfile ReconcileProfile(
        GacDefenseStrategyProfile profile,
        GacLeague league,
        IReadOnlyCollection<GacTeamPreset> presets)
    {
        GacBoardLayout layout = GacBoardLayouts.Get(league, profile.Format);
        Dictionary<Guid, GacTeamPreset> presetsById = presets.ToDictionary(item => item.Id);
        var pendingPins = profile.Slots
            .Where(slot => slot.PinnedTeamPresetId is not null)
            .Select(slot => new PendingPin(slot.Zone, slot.PinnedTeamPresetId!.Value))
            .Where(pin => presetsById.ContainsKey(pin.PresetId))
            .ToList();
        var slots = new List<GacDefenseTemplateSlot>(layout.TotalDefenseSlots);

        foreach (GacBoardDefenseSlot layoutSlot in layout.Slots.OrderBy(slot => slot.Position))
        {
            int matchingIndex = pendingPins.FindIndex(pin =>
                string.Equals(pin.Zone, layoutSlot.Zone, StringComparison.OrdinalIgnoreCase) &&
                presetsById[pin.PresetId].Squad.IsFleet == layoutSlot.IsFleet);
            if (matchingIndex < 0)
            {
                matchingIndex = pendingPins.FindIndex(pin =>
                    presetsById[pin.PresetId].Squad.IsFleet == layoutSlot.IsFleet);
            }

            Guid? pinnedId = null;
            if (matchingIndex >= 0)
            {
                pinnedId = pendingPins[matchingIndex].PresetId;
                pendingPins.RemoveAt(matchingIndex);
            }

            slots.Add(new GacDefenseTemplateSlot(layoutSlot.Position, layoutSlot.Zone, pinnedId));
        }

        return profile with { Slots = slots };
    }

    private static void ValidateSlots(IReadOnlyCollection<GacDefenseTemplateSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count is < 1 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(slots), "A defense template requires between 1 and 24 slots.");
        }

        if (slots.Any(slot => slot.Position < 1 || string.IsNullOrWhiteSpace(slot.Zone)))
        {
            throw new ArgumentException("Every defense template slot needs a positive position and a zone.", nameof(slots));
        }

        if (slots.Select(slot => slot.Position).Distinct().Count() != slots.Count)
        {
            throw new ArgumentException("Defense template slot positions must be unique.", nameof(slots));
        }
    }

    private static bool MatchesZone(GacTeamPresetDetails preset, string zone) =>
        preset.Squad.IsFleet == IsFleetZone(zone);

    private static bool IsFleetZone(string zone) =>
        zone.Contains("flota", StringComparison.OrdinalIgnoreCase) ||
        zone.Contains("fleet", StringComparison.OrdinalIgnoreCase);

    private static bool Conflicts(
        GacTeamPresetDetails preset,
        HashSet<Guid> usedPresets,
        HashSet<string> usedUnits) =>
        usedPresets.Contains(preset.Id) ||
        preset.Squad.AllUnits.Any(unit => usedUnits.Contains(unit.DefinitionId));

    private static int PresetUsePriority(GacTeamPresetDetails preset) => preset.Use switch
    {
        GacPlannerTeamUse.Defense => 0,
        GacPlannerTeamUse.Flexible => 1,
        _ => 2
    };

    private static long TeamPower(GacTeamPresetDetails preset) =>
        preset.Squad.AllUnits.Sum(unit => unit.GalacticPower ?? 0L);

    private static GacDefensePresetSummary ToSummary(GacTeamPreset preset) => new(
        preset.Id,
        preset.Name,
        preset.Use,
        preset.Squad.IsFleet);

    private static void ValidateFormat(GacFormat format)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.");
        }
    }

    private static string FormatName(GacFormat format) => format switch
    {
        GacFormat.ThreeVsThree => "3v3",
        GacFormat.FiveVsFive => "5v5",
        _ => format.ToString()
    };

    internal sealed record Generation(
        IReadOnlyCollection<GacGeneratedDefenseAssignment> Assignments,
        IReadOnlyCollection<string> Warnings);

    private sealed record PendingPin(string Zone, Guid PresetId);
}
