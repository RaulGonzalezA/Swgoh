using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Gac;

internal sealed class LearningGacPlannerService(
    GacPlannerService inner,
    IGacPersonalLearningService personalLearningService,
    IGacPersonalBattleRepository personalBattleRepository,
    IGacRoundPlanRepository planRepository,
    IGacGeneratedTeamLifecycleService generatedTeamLifecycleService,
    IPlayerProfileService playerProfileService,
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

        GacPlannerLookup before = await inner.GetCurrentAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (before.State is not null)
        {
            await ValidateDatacronsAsync(allyCode, before.State, input, cancellationToken).ConfigureAwait(false);
        }

        GacPlannerLookup lookup;
        using (writeContext.Begin(expectedVersion))
        {
            lookup = await inner
                .SaveCurrentAsync(allyCode, input, cancellationToken)
                .ConfigureAwait(false);
        }

        if (lookup.State is not null && HasDatacronAssignments(input))
        {
            await PersistDatacronAssignmentsAsync(
                lookup.State.Plan.Id,
                input,
                cancellationToken).ConfigureAwait(false);
            lookup = await inner.GetCurrentAsync(allyCode, cancellationToken).ConfigureAwait(false);
        }

        GacPlannerLookup enriched = await EnrichAsync(lookup, cancellationToken).ConfigureAwait(false);
        if (enriched.State is not null)
        {
            await personalLearningService
                .SyncPlannerStateAsync(enriched.State, cancellationToken)
                .ConfigureAwait(false);
        }

        return enriched;
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

    private async Task ValidateDatacronsAsync(
        long allyCode,
        GacPlannerState state,
        SaveCurrentGacRoundPlan input,
        CancellationToken cancellationToken)
    {
        string[] activeIds =
        [
            .. input.OwnDefenses
                .Select(item => GacDatacronRules.NormalizeId(item.DatacronId))
                .Concat(input.Attacks
                    .Where(attack => attack.Status == GacAttackPlanStatus.Planned)
                    .Select(attack => GacDatacronRules.NormalizeId(attack.DatacronId)))
                .Where(id => id is not null)
                .Select(id => id!)
        ];
        if (activeIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != activeIds.Length)
        {
            throw new ArgumentException(
                "A datacron can only be assigned once across active GAC defense and planned attacks.",
                nameof(input));
        }

        if (!activeIds.Any())
        {
            return;
        }

        PlayerProfile? player = await playerProfileService.GetAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            throw new ArgumentException("Player datacrons are not available. Refresh the player before assigning one.");
        }

        Dictionary<string, GacPlannerDatacronDetails> datacrons = player.Datacrons
            .Select(GacDatacronRules.ToDetails)
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        Dictionary<Guid, GacTeamPresetDetails> teams = state.Presets.ToDictionary(item => item.Id);

        foreach (SaveGacOwnDefenseAssignment assignment in input.OwnDefenses)
        {
            ValidateAssignmentDatacron(assignment.TeamPresetId, assignment.DatacronId, teams, datacrons);
        }

        foreach (SaveGacAttackAssignment assignment in input.Attacks)
        {
            ValidateAssignmentDatacron(assignment.TeamPresetId, assignment.DatacronId, teams, datacrons);
        }
    }

    private static void ValidateAssignmentDatacron(
        Guid teamPresetId,
        string? datacronId,
        IReadOnlyDictionary<Guid, GacTeamPresetDetails> teams,
        IReadOnlyDictionary<string, GacPlannerDatacronDetails> datacrons)
    {
        string? normalized = GacDatacronRules.NormalizeId(datacronId);
        if (normalized is null)
        {
            return;
        }

        if (!teams.TryGetValue(teamPresetId, out GacTeamPresetDetails? team))
        {
            throw new ArgumentException($"Team preset '{teamPresetId}' is not available for datacron assignment.");
        }

        if (!datacrons.TryGetValue(normalized, out GacPlannerDatacronDetails? datacron))
        {
            throw new ArgumentException($"Datacron '{normalized}' is not available in the player's current inventory.");
        }

        if (!GacDatacronRules.IsEligible(team, datacron))
        {
            throw new ArgumentException(
                $"Datacron '{normalized}' is not eligible for team '{team.Name}' because of squad type, tier or relic requirements.");
        }
    }

    private async Task PersistDatacronAssignmentsAsync(
        string planId,
        SaveCurrentGacRoundPlan input,
        CancellationToken cancellationToken)
    {
        GacRoundPlan plan = await planRepository.FindByIdAsync(planId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The current GAC round plan disappeared while assigning datacrons.");
        long currentVersion = await planRepository.GetVersionAsync(planId, cancellationToken).ConfigureAwait(false);

        GacOwnDefenseAssignment[] ownDefenses =
        [
            .. plan.OwnDefenses.Select(current =>
            {
                SaveGacOwnDefenseAssignment? requested = input.OwnDefenses.FirstOrDefault(item =>
                    item.Id == current.Id ||
                    (item.Id is null &&
                     item.TeamPresetId == current.TeamPresetId &&
                     string.Equals(item.Zone, current.Zone, StringComparison.OrdinalIgnoreCase)));
                return GacOwnDefenseAssignment.Create(
                    current.Id,
                    current.Zone,
                    current.TeamPresetId,
                    requested?.DatacronId);
            })
        ];
        GacAttackAssignment[] attacks =
        [
            .. plan.Attacks.Select(current =>
            {
                SaveGacAttackAssignment? requested = input.Attacks.FirstOrDefault(item =>
                    item.Id == current.Id ||
                    (item.Id is null &&
                     item.DefenseId == current.DefenseId &&
                     item.TeamPresetId == current.TeamPresetId &&
                     item.Attempt == current.Attempt));
                return GacAttackAssignment.Create(
                    current.Id,
                    current.DefenseId,
                    current.TeamPresetId,
                    current.Attempt,
                    current.Status,
                    current.Notes,
                    requested?.DatacronId);
            })
        ];

        plan.Replace(ownDefenses, plan.VisibleDefenses, attacks, DateTimeOffset.UtcNow);
        bool saved = await planRepository.TrySaveAsync(plan, currentVersion, cancellationToken).ConfigureAwait(false);
        if (!saved)
        {
            throw new GacPlannerConcurrencyException(
                "The GAC plan changed while datacrons were being assigned. Reload and recalculate.");
        }
    }

    private async Task<GacPlannerLookup> EnrichAsync(
        GacPlannerLookup lookup,
        CancellationToken cancellationToken)
    {
        if (lookup.State is null)
        {
            return lookup;
        }

        GacRoundPlanDetails originalPlan = lookup.State.Plan;
        Task<long> versionTask = planRepository.GetVersionAsync(originalPlan.Id, cancellationToken);
        Task<GacRoundPlan?> domainPlanTask = planRepository.FindByIdAsync(originalPlan.Id, cancellationToken);
        Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(originalPlan.PlayerAllyCode, cancellationToken);
        Task<PlayerProfile?> opponentTask = playerProfileService.GetAsync(originalPlan.OpponentAllyCode, cancellationToken);
        await Task.WhenAll(versionTask, domainPlanTask, playerTask, opponentTask).ConfigureAwait(false);

        long version = await versionTask.ConfigureAwait(false);
        GacRoundPlan? domainPlan = await domainPlanTask.ConfigureAwait(false);
        PlayerProfile? player = await playerTask.ConfigureAwait(false);
        PlayerProfile? opponent = await opponentTask.ConfigureAwait(false);

        Dictionary<Guid, string?> defenseDatacrons = domainPlan?.OwnDefenses
            .ToDictionary(item => item.Id, item => item.DatacronId) ?? [];
        Dictionary<Guid, string?> attackDatacrons = domainPlan?.Attacks
            .ToDictionary(item => item.Id, item => item.DatacronId) ?? [];

        GacOwnDefenseAssignmentDetails[] defenses =
        [
            .. originalPlan.OwnDefenses.Select(defense => defense with
            {
                DatacronId = defenseDatacrons.GetValueOrDefault(defense.Id)
            })
        ];
        GacRoundPlanDetails plan = originalPlan with
        {
            Version = version,
            OwnDefenses = defenses
        };

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
                    Banners = bannersByAttackId.GetValueOrDefault(attack.Id),
                    DatacronId = attackDatacrons.GetValueOrDefault(attack.Id)
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
        GacPlannerDatacronDetails[] playerDatacrons =
        [
            .. (player?.Datacrons ?? []).Select(GacDatacronRules.ToDetails)
                .OrderByDescending(item => item.Tier)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
        ];
        GacPlannerDatacronDetails[] opponentDatacrons =
        [
            .. (opponent?.Datacrons ?? []).Select(GacDatacronRules.ToDetails)
                .OrderByDescending(item => item.Tier)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
        ];

        return lookup with
        {
            State = lookup.State with
            {
                Plan = plan,
                Presets = reusablePresets,
                Datacrons = playerDatacrons,
                OpponentDatacrons = opponentDatacrons
            }
        };
    }

    private static bool HasDatacronAssignments(SaveCurrentGacRoundPlan input) =>
        input.OwnDefenses.Any(item => GacDatacronRules.NormalizeId(item.DatacronId) is not null) ||
        input.Attacks.Any(item => GacDatacronRules.NormalizeId(item.DatacronId) is not null);
}
