using Swgoh.Application.Abstractions;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacGeneratedDefenseCleanupService
{
    Task<GacGeneratedDefenseCleanupResult> DeleteAsync(
        long allyCode,
        GacFormat format,
        CancellationToken cancellationToken = default);
}

public sealed record GacGeneratedDefenseCleanupResult(
    GacFormat Format,
    int DeletedPresets,
    int RemovedDefenseAssignments,
    IReadOnlyCollection<string> DeletedTeamNames,
    IReadOnlyCollection<string> Warnings);

internal sealed class GacGeneratedDefenseCleanupService(
    IGacDefenseStrategyRepository strategyRepository,
    IGacTeamPresetRepository presetRepository,
    IGacPlannerService plannerService,
    IGacGeneratedTeamLifecycleService generatedTeamLifecycleService,
    IClock clock) : IGacGeneratedDefenseCleanupService
{
    public async Task<GacGeneratedDefenseCleanupResult> DeleteAsync(
        long allyCode,
        GacFormat format,
        CancellationToken cancellationToken = default)
    {
        ValidateFormat(format);

        IReadOnlySet<Guid> generatedIds = await generatedTeamLifecycleService
            .GetGeneratedPresetIdsAsync(
                allyCode,
                format,
                GacGeneratedTeamOrigin.SmartDefense,
                cancellationToken)
            .ConfigureAwait(false);
        if (generatedIds.Count == 0)
        {
            return new GacGeneratedDefenseCleanupResult(format, 0, 0, [], []);
        }

        IReadOnlyCollection<GacTeamPreset> presets = await presetRepository
            .GetAsync(allyCode, format, cancellationToken)
            .ConfigureAwait(false);
        GacTeamPreset[] generated =
        [
            .. presets.Where(preset => generatedIds.Contains(preset.Id))
        ];
        if (generated.Length == 0)
        {
            await generatedTeamLifecycleService.ForgetAsync(generatedIds.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            return new GacGeneratedDefenseCleanupResult(format, 0, 0, [], []);
        }

        var warnings = new List<string>();
        GacPlannerLookup current = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);

        HashSet<Guid> protectedByAttack = current.State?.Plan.Attacks
            .Where(attack => attack.Status != GacAttackPlanStatus.Cancelled && generatedIds.Contains(attack.Team.Id))
            .Select(attack => attack.Team.Id)
            .ToHashSet() ?? [];
        if (protectedByAttack.Count > 0)
        {
            warnings.Add(
                $"Se conservan {protectedByAttack.Count} equipo(s) generados porque están usados por ataques activos de la ronda.");
        }

        HashSet<Guid> deletableIds = generatedIds
            .Where(id => !protectedByAttack.Contains(id))
            .ToHashSet();
        int removedAssignments = await RemoveCurrentDefenseAssignmentsAsync(
            allyCode,
            format,
            current,
            deletableIds,
            cancellationToken).ConfigureAwait(false);

        await CleanStrategyReferencesAsync(
            allyCode,
            format,
            deletableIds,
            cancellationToken).ConfigureAwait(false);

        var deletedNames = new List<string>();
        var deletedIds = new List<Guid>();
        foreach (GacTeamPreset preset in generated.Where(item => deletableIds.Contains(item.Id)))
        {
            bool deleted = await presetRepository.DeleteAsync(preset.Id, cancellationToken).ConfigureAwait(false);
            if (deleted)
            {
                deletedNames.Add(preset.Name);
                deletedIds.Add(preset.Id);
            }
            else
            {
                warnings.Add($"No se ha podido borrar el equipo generado '{preset.Name}'.");
            }
        }

        await generatedTeamLifecycleService
            .ForgetAsync(deletedIds, cancellationToken)
            .ConfigureAwait(false);

        return new GacGeneratedDefenseCleanupResult(
            format,
            deletedNames.Count,
            removedAssignments,
            deletedNames,
            warnings);
    }

    private async Task<int> RemoveCurrentDefenseAssignmentsAsync(
        long allyCode,
        GacFormat format,
        GacPlannerLookup current,
        IReadOnlySet<Guid> deletableIds,
        CancellationToken cancellationToken)
    {
        GacPlannerState? state = current.State;
        if (!current.IsAvailable || state is null || state.Plan.Format != format || deletableIds.Count == 0)
        {
            return 0;
        }

        GacOwnDefenseAssignmentDetails[] removed =
        [
            .. state.Plan.OwnDefenses.Where(item => deletableIds.Contains(item.Team.Id))
        ];
        if (removed.Length == 0)
        {
            return 0;
        }

        IReadOnlyCollection<SaveGacOwnDefenseAssignment> ownDefenses =
        [
            .. state.Plan.OwnDefenses
                .Where(item => !deletableIds.Contains(item.Team.Id))
                .Select(item => new SaveGacOwnDefenseAssignment(
                    item.Id,
                    item.Zone,
                    item.Team.Id,
                    item.DatacronId))
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
                item.Notes,
                item.DatacronId))
        ];

        GacPlannerLookup saved = await plannerService
            .SaveCurrentAsync(
                allyCode,
                new SaveCurrentGacRoundPlan(ownDefenses, visibleDefenses, attacks, state.Plan.Version),
                cancellationToken)
            .ConfigureAwait(false);
        if (!saved.IsAvailable || saved.State is null)
        {
            throw new InvalidOperationException(
                saved.Message ?? "No se han podido retirar las defensas generadas del plan actual.");
        }

        return removed.Length;
    }

    private async Task CleanStrategyReferencesAsync(
        long allyCode,
        GacFormat format,
        IReadOnlySet<Guid> deletableIds,
        CancellationToken cancellationToken)
    {
        if (deletableIds.Count == 0)
        {
            return;
        }

        GacDefenseStrategyProfile? profile = await strategyRepository
            .FindAsync(allyCode, format, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            return;
        }

        GacDefenseTemplateSlot[] slots =
        [
            .. profile.Slots.Select(slot =>
                slot.PinnedTeamPresetId is Guid pinned && deletableIds.Contains(pinned)
                    ? slot with { PinnedTeamPresetId = null }
                    : slot)
        ];
        Guid[] reserved =
        [
            .. profile.ReservedAttackPresetIds.Where(id => !deletableIds.Contains(id))
        ];

        bool changed = slots.Where((slot, index) => slot != profile.Slots.ElementAt(index)).Any() ||
            reserved.Length != profile.ReservedAttackPresetIds.Count;
        if (!changed)
        {
            return;
        }

        await strategyRepository
            .UpsertAsync(profile with
            {
                Slots = slots,
                ReservedAttackPresetIds = reserved,
                UpdatedAtUtc = clock.UtcNow
            }, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidateFormat(GacFormat format)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.");
        }
    }
}
