using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class LearningGacPlannerService(
    GacPlannerService inner,
    IGacPersonalLearningService personalLearningService,
    IGacPersonalBattleRepository personalBattleRepository,
    IGacRoundPlanRepository planRepository,
    IGacGeneratedTeamLifecycleService generatedTeamLifecycleService,
    GacPlannerWriteContext writeContext) : IGacPlannerService
{
    public async Task<GacPlannerLookup> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        GacPlannerLookup lookup = await inner.GetCurrentAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return await EnrichAsync(lookup, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GacPlannerLookup> SaveCurrentAsync(
        long allyCode,
        SaveCurrentGacRoundPlan input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ExpectedVersion is not long expectedVersion)
        {
            throw new GacPlannerConcurrencyException(
                "A plan version is required for every GAC write. Reload the round and try again.");
        }

        GacPlannerLookup lookup;
        using (writeContext.Begin(expectedVersion))
        {
            lookup = await inner
                .SaveCurrentAsync(allyCode, input, cancellationToken)
                .ConfigureAwait(false);
        }

        if (lookup.State is not null)
        {
            await personalLearningService
                .SyncPlannerStateAsync(lookup.State, cancellationToken)
                .ConfigureAwait(false);
        }

        return await EnrichAsync(lookup, cancellationToken).ConfigureAwait(false);
    }

    public Task<GacTeamPresetDetails> CreatePresetAsync(
        long allyCode,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default) =>
        inner.CreatePresetAsync(allyCode, input, cancellationToken);

    public Task<GacTeamPresetDetails?> UpdatePresetAsync(
        long allyCode,
        Guid id,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default) =>
        inner.UpdatePresetAsync(allyCode, id, input, cancellationToken);

    public Task<bool> DeletePresetAsync(
        long allyCode,
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.DeletePresetAsync(allyCode, id, cancellationToken);

    private async Task<GacPlannerLookup> EnrichAsync(
        GacPlannerLookup lookup,
        CancellationToken cancellationToken)
    {
        if (lookup.State is null)
        {
            return lookup;
        }

        long version = await planRepository
            .GetVersionAsync(lookup.State.Plan.Id, cancellationToken)
            .ConfigureAwait(false);
        GacRoundPlanDetails plan = lookup.State.Plan with { Version = version };

        if (plan.Attacks.Count > 0)
        {
            IReadOnlyCollection<GacPersonalBattleObservation> observations = await personalBattleRepository
                .GetRoundAsync(
                    plan.PlayerAllyCode,
                    plan.EventInstanceId,
                    plan.RoundNumber,
                    cancellationToken)
                .ConfigureAwait(false);
            Dictionary<Guid, int?> bannersByAttackId = observations
                .GroupBy(observation => observation.AttackId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.RecordedAtUtc).First().Banners);
            GacAttackAssignmentDetails[] attacks =
            [
                .. plan.Attacks.Select(attack => attack with
                {
                    Banners = bannersByAttackId.GetValueOrDefault(attack.Id)
                })
            ];
            plan = plan with { Attacks = attacks };
        }

        IReadOnlySet<Guid> generatedPresetIds = await generatedTeamLifecycleService
            .GetGeneratedPresetIdsAsync(
                plan.PlayerAllyCode,
                plan.Format,
                origin: null,
                cancellationToken)
            .ConfigureAwait(false);
        GacPlannerState lifecycleState = lookup.State with { Plan = plan };
        await generatedTeamLifecycleService
            .PruneUnreferencedAsync(plan.PlayerAllyCode, lifecycleState, cancellationToken)
            .ConfigureAwait(false);

        GacTeamPresetDetails[] reusablePresets =
        [
            .. lookup.State.Presets.Where(preset => !generatedPresetIds.Contains(preset.Id))
        ];

        return lookup with
        {
            State = lookup.State with
            {
                Plan = plan,
                Presets = reusablePresets
            }
        };
    }
}
