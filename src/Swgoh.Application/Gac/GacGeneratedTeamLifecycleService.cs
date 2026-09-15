using Swgoh.Application.Abstractions;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public enum GacGeneratedTeamOrigin
{
    SmartDefense = 1,
    CounterEngine = 2
}

public sealed record GacGeneratedTeamLifecycleEntry(
    Guid PresetId,
    long AllyCode,
    GacFormat Format,
    GacGeneratedTeamOrigin Origin,
    string GenerationId,
    string? RoundPlanId,
    DateTimeOffset CreatedAtUtc);

public interface IGacGeneratedTeamLifecycleRepository
{
    Task<IReadOnlyCollection<GacGeneratedTeamLifecycleEntry>> GetAsync(
        long allyCode,
        GacFormat? format,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        GacGeneratedTeamLifecycleEntry entry,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid presetId,
        CancellationToken cancellationToken = default);
}

public interface IGacGeneratedTeamLifecycleService
{
    Task<IReadOnlySet<Guid>> GetGeneratedPresetIdsAsync(
        long allyCode,
        GacFormat format,
        GacGeneratedTeamOrigin? origin = null,
        CancellationToken cancellationToken = default);

    Task RegisterAsync(
        long allyCode,
        GacFormat format,
        GacGeneratedTeamOrigin origin,
        string generationId,
        string roundPlanId,
        IReadOnlyCollection<Guid> presetIds,
        CancellationToken cancellationToken = default);

    Task<int> PruneUnreferencedAsync(
        long allyCode,
        GacPlannerState state,
        CancellationToken cancellationToken = default);

    Task ForgetAsync(
        IReadOnlyCollection<Guid> presetIds,
        CancellationToken cancellationToken = default);
}

internal sealed class GacGeneratedTeamLifecycleService(
    IGacGeneratedTeamLifecycleRepository repository,
    IGacTeamPresetRepository presetRepository,
    IGacDefenseStrategyRepository strategyRepository,
    IClock clock) : IGacGeneratedTeamLifecycleService
{
    public async Task<IReadOnlySet<Guid>> GetGeneratedPresetIdsAsync(
        long allyCode,
        GacFormat format,
        GacGeneratedTeamOrigin? origin = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFormat(format);
        IReadOnlyCollection<GacGeneratedTeamLifecycleEntry> entries = await repository
            .GetAsync(allyCode, format, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyCollection<GacTeamPreset> presets = await presetRepository
            .GetAsync(allyCode, format, cancellationToken)
            .ConfigureAwait(false);

        var result = entries
            .Where(entry => origin is null || entry.Origin == origin)
            .Select(entry => entry.PresetId)
            .ToHashSet();

        foreach (GacTeamPreset preset in presets)
        {
            if (TryInferLegacyOrigin(preset, out GacGeneratedTeamOrigin inferred) &&
                (origin is null || origin == inferred))
            {
                result.Add(preset.Id);
            }
        }

        return result;
    }

    public async Task RegisterAsync(
        long allyCode,
        GacFormat format,
        GacGeneratedTeamOrigin origin,
        string generationId,
        string roundPlanId,
        IReadOnlyCollection<Guid> presetIds,
        CancellationToken cancellationToken = default)
    {
        ValidateFormat(format);
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unsupported generated team origin.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(generationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roundPlanId);
        ArgumentNullException.ThrowIfNull(presetIds);

        foreach (Guid presetId in presetIds.Distinct())
        {
            if (presetId == Guid.Empty)
            {
                throw new ArgumentException("Generated preset ID cannot be empty.", nameof(presetIds));
            }

            await repository.UpsertAsync(
                new GacGeneratedTeamLifecycleEntry(
                    presetId,
                    allyCode,
                    format,
                    origin,
                    generationId.Trim(),
                    roundPlanId.Trim(),
                    clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> PruneUnreferencedAsync(
        long allyCode,
        GacPlannerState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        IReadOnlySet<Guid> generatedIds = await GetGeneratedPresetIdsAsync(
            allyCode,
            state.Plan.Format,
            origin: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (generatedIds.Count == 0)
        {
            return 0;
        }

        HashSet<Guid> protectedIds = state.Plan.OwnDefenses
            .Select(defense => defense.Team.Id)
            .Concat(state.Plan.Attacks
                .Where(attack => attack.Status != GacAttackPlanStatus.Cancelled)
                .Select(attack => attack.Team.Id))
            .ToHashSet();
        Guid[] staleIds =
        [
            .. generatedIds.Where(id => !protectedIds.Contains(id))
        ];
        if (staleIds.Length == 0)
        {
            return 0;
        }

        await CleanStrategyReferencesAsync(
            allyCode,
            state.Plan.Format,
            staleIds.ToHashSet(),
            cancellationToken).ConfigureAwait(false);

        int deleted = 0;
        foreach (Guid presetId in staleIds)
        {
            if (await presetRepository.DeleteAsync(presetId, cancellationToken).ConfigureAwait(false))
            {
                deleted++;
            }

            await repository.DeleteAsync(presetId, cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    public async Task ForgetAsync(
        IReadOnlyCollection<Guid> presetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presetIds);
        foreach (Guid presetId in presetIds.Where(id => id != Guid.Empty).Distinct())
        {
            await repository.DeleteAsync(presetId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CleanStrategyReferencesAsync(
        long allyCode,
        GacFormat format,
        IReadOnlySet<Guid> deletedIds,
        CancellationToken cancellationToken)
    {
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
                slot.PinnedTeamPresetId is Guid pinned && deletedIds.Contains(pinned)
                    ? slot with { PinnedTeamPresetId = null }
                    : slot)
        ];
        Guid[] reserved =
        [
            .. profile.ReservedAttackPresetIds.Where(id => !deletedIds.Contains(id))
        ];
        bool changed = slots.Where((slot, index) => slot != profile.Slots.ElementAt(index)).Any() ||
            reserved.Length != profile.ReservedAttackPresetIds.Count;
        if (!changed)
        {
            return;
        }

        await strategyRepository.UpsertAsync(profile with
        {
            Slots = slots,
            ReservedAttackPresetIds = reserved,
            UpdatedAtUtc = clock.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryInferLegacyOrigin(
        GacTeamPreset preset,
        out GacGeneratedTeamOrigin origin)
    {
        if (preset.Use != GacPlannerTeamUse.Defense &&
            preset.Name.StartsWith("Auto ATK ·", StringComparison.OrdinalIgnoreCase))
        {
            origin = GacGeneratedTeamOrigin.CounterEngine;
            return true;
        }

        if (preset.Use == GacPlannerTeamUse.Defense &&
            preset.Name.StartsWith("Auto ·", StringComparison.OrdinalIgnoreCase))
        {
            origin = GacGeneratedTeamOrigin.SmartDefense;
            return true;
        }

        origin = default;
        return false;
    }

    private static void ValidateFormat(GacFormat format)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.");
        }
    }
}
