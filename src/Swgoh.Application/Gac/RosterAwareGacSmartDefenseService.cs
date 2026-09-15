using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class RosterAwareGacSmartDefenseService(
    GacDefenseStrategyService strategyService,
    IGacPlannerService plannerService,
    ICurrentGacScoutingService scoutingService,
    IGacPersonalLearningService personalLearningService,
    IGacRosterDefenseCandidateProvider rosterCandidateProvider,
    IGacGeneratedTeamLifecycleService generatedTeamLifecycleService) : IGacSmartDefenseService
{
    private const int HistoryRoundLimit = 30;

    public async Task<GacSmartDefenseGenerationResult> GenerateCurrentAsync(
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
        Task<GacDefenseStrategySnapshot> strategyTask = strategyService.GetAsync(
            allyCode,
            state.Plan.Format,
            cancellationToken);
        Task<CurrentGacScoutingResult> scoutingTask = scoutingService.GetAsync(
            allyCode,
            state.Plan.Format,
            HistoryRoundLimit,
            cancellationToken);
        Task<IReadOnlyCollection<GacPersonalMatchupStatistics>> personalTask = personalLearningService
            .GetStatisticsAsync(allyCode, state.Plan.Format, cancellationToken);
        Task<IReadOnlySet<Guid>> generatedPresetIdsTask = generatedTeamLifecycleService
            .GetGeneratedPresetIdsAsync(
                allyCode,
                state.Plan.Format,
                origin: null,
                cancellationToken: cancellationToken);

        await Task.WhenAll(strategyTask, scoutingTask, personalTask, generatedPresetIdsTask).ConfigureAwait(false);
        GacDefenseStrategySnapshot strategy = await strategyTask.ConfigureAwait(false);
        CurrentGacScoutingResult scouting = await scoutingTask.ConfigureAwait(false);
        IReadOnlyCollection<GacPersonalMatchupStatistics> personal = await personalTask.ConfigureAwait(false);
        IReadOnlySet<Guid> generatedPresetIds = await generatedPresetIdsTask.ConfigureAwait(false);
        GacDefenseStrategyProfile reusableProfile = RemoveGeneratedReferences(strategy.Profile, generatedPresetIds);

        string[] consumedUnits =
        [
            .. state.Plan.Attacks
                .Where(attack => attack.Status is GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed)
                .SelectMany(attack => attack.Team.Squad.AllUnits)
                .Select(unit => unit.DefinitionId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
        GacRosterDefenseCandidateSet rosterCandidates = await rosterCandidateProvider.BuildAsync(
            allyCode,
            state.Plan.Format,
            reusableProfile,
            state.Presets,
            scouting.BattlePlan,
            consumedUnits,
            cancellationToken).ConfigureAwait(false);
        GacTeamPresetDetails[] candidates =
        [
            .. state.Presets,
            .. rosterCandidates.Candidates
        ];

        GacSmartDefenseService.SmartGeneration generation = GacSmartDefenseService.Generate(
            reusableProfile,
            candidates,
            scouting,
            personal,
            state.PlayerDatacrons);
        generation = generation with
        {
            Warnings = [.. rosterCandidates.Warnings, .. generation.Warnings]
        };

        if (!apply)
        {
            return ToResult(state.Plan.Format, false, generation, state.Plan.UpdatedAtUtc, scouting);
        }

        IReadOnlyDictionary<Guid, string?> temporaryDatacrons = AllocateDefenseDatacrons(
            generation.Assignments,
            candidates,
            state);
        string generationId = Guid.NewGuid().ToString("N");
        GacGeneratedPresetMaterialization materialization = await GacRosterDefenseCandidateService.MaterializeAsync(
            plannerService,
            allyCode,
            state.Plan.Format,
            generation.Assignments,
            rosterCandidates,
            cancellationToken).ConfigureAwait(false);
        GacSmartDefenseAssignment[] appliedAssignments =
        [
            .. generation.Assignments.Select(item =>
                GacRosterDefenseCandidateService.Remap(item, materialization.IdMap))
        ];
        Dictionary<Guid, string?> appliedDatacrons = temporaryDatacrons.ToDictionary(
            item => materialization.IdMap.GetValueOrDefault(item.Key, item.Key),
            item => item.Value);
        GacSmartDefenseService.SmartGeneration appliedGeneration = generation with
        {
            Assignments = appliedAssignments
        };

        GacPlannerLookup saved;
        try
        {
            saved = await SaveAsync(
                allyCode,
                state,
                appliedAssignments,
                appliedDatacrons,
                cancellationToken).ConfigureAwait(false);
            if (!saved.IsAvailable || saved.State is null)
            {
                throw new InvalidOperationException(saved.Message ?? "The smart defense could not be applied.");
            }
        }
        catch
        {
            await RollbackAsync(allyCode, materialization).ConfigureAwait(false);
            throw;
        }

        IReadOnlyCollection<string> lifecycleWarnings = await CompleteLifecycleAsync(
            allyCode,
            state,
            saved.State!,
            generationId,
            materialization.CreatedPresetIds,
            cancellationToken).ConfigureAwait(false);
        if (lifecycleWarnings.Count > 0)
        {
            appliedGeneration = appliedGeneration with
            {
                Warnings = [.. appliedGeneration.Warnings, .. lifecycleWarnings]
            };
        }

        return ToResult(
            state.Plan.Format,
            true,
            appliedGeneration,
            saved.State!.Plan.UpdatedAtUtc,
            scouting);
    }

    private async Task<IReadOnlyCollection<string>> CompleteLifecycleAsync(
        long allyCode,
        GacPlannerState originalState,
        GacPlannerState savedState,
        string generationId,
        IReadOnlyCollection<Guid> createdPresetIds,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        try
        {
            await generatedTeamLifecycleService.RegisterAsync(
                allyCode,
                originalState.Plan.Format,
                GacGeneratedTeamOrigin.SmartDefense,
                generationId,
                originalState.Plan.Id,
                createdPresetIds,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            warnings.Add(
                "La defensa se ha aplicado, pero no se pudo persistir toda la metadata de lifecycle; la compatibilidad legacy la mantendrá aislada.");
        }

        try
        {
            await generatedTeamLifecycleService
                .PruneUnreferencedAsync(allyCode, savedState, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            warnings.Add(
                "La defensa se ha aplicado, pero la limpieza automática de equipos generados anteriores no pudo completarse.");
        }

        return warnings;
    }

    private async Task<GacPlannerLookup> SaveAsync(
        long allyCode,
        GacPlannerState state,
        IReadOnlyCollection<GacSmartDefenseAssignment> assignments,
        IReadOnlyDictionary<Guid, string?> datacronsByTeamId,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SaveGacOwnDefenseAssignment> ownDefenses =
        [
            .. assignments.Select(item =>
            {
                GacOwnDefenseAssignmentDetails? existing = state.Plan.OwnDefenses.FirstOrDefault(defense =>
                    defense.Team.Id == item.TeamPresetId &&
                    string.Equals(defense.Zone, item.Zone, StringComparison.OrdinalIgnoreCase));
                return new SaveGacOwnDefenseAssignment(
                    existing?.Id,
                    item.Zone,
                    item.TeamPresetId,
                    datacronsByTeamId.GetValueOrDefault(item.TeamPresetId));
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
                item.Notes,
                item.DatacronId))
        ];

        return await plannerService.SaveCurrentAsync(
            allyCode,
            new SaveCurrentGacRoundPlan(ownDefenses, visibleDefenses, attacks, state.Plan.Version),
            cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<Guid, string?> AllocateDefenseDatacrons(
        IReadOnlyCollection<GacSmartDefenseAssignment> assignments,
        IReadOnlyCollection<GacTeamPresetDetails> candidates,
        GacPlannerState state)
    {
        Dictionary<Guid, GacTeamPresetDetails> teams = candidates.ToDictionary(item => item.Id);
        var used = state.Plan.Attacks
            .Where(attack => attack.Status == GacAttackPlanStatus.Planned)
            .Select(attack => attack.DatacronId)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<Guid, string?>();

        foreach (GacSmartDefenseAssignment assignment in assignments.OrderBy(item => item.Position))
        {
            if (!teams.TryGetValue(assignment.TeamPresetId, out GacTeamPresetDetails? team) || team.Squad.IsFleet)
            {
                result[assignment.TeamPresetId] = null;
                continue;
            }

            GacPlannerDatacronDetails? datacron = GacDatacronRules.BestEligible(
                team,
                state.PlayerDatacrons,
                used);
            result[assignment.TeamPresetId] = datacron?.Id;
            if (datacron is not null)
            {
                used.Add(datacron.Id);
            }
        }

        return result;
    }

    private Task RollbackAsync(
        long allyCode,
        GacGeneratedPresetMaterialization materialization) =>
        GacRosterDefenseCandidateService.RollbackMaterializationAsync(
            plannerService,
            allyCode,
            materialization.CreatedPresetIds);

    private static GacDefenseStrategyProfile RemoveGeneratedReferences(
        GacDefenseStrategyProfile profile,
        IReadOnlySet<Guid> generatedPresetIds) =>
        profile with
        {
            Slots =
            [
                .. profile.Slots.Select(slot =>
                    slot.PinnedTeamPresetId is Guid pinned && generatedPresetIds.Contains(pinned)
                        ? slot with { PinnedTeamPresetId = null }
                        : slot)
            ],
            ReservedAttackPresetIds =
            [
                .. profile.ReservedAttackPresetIds.Where(id => !generatedPresetIds.Contains(id))
            ]
        };

    private static GacSmartDefenseGenerationResult ToResult(
        GacFormat format,
        bool applied,
        GacSmartDefenseService.SmartGeneration generation,
        DateTimeOffset? updatedAtUtc,
        CurrentGacScoutingResult scouting) => new(
        format,
        applied,
        generation.Assignments,
        generation.Warnings,
        updatedAtUtc,
        scouting.Scouting?.RoundsAnalyzed ?? 0,
        scouting.Scouting?.FullClearRate,
        "SmartBalancedRosterGlobalOptimization");
}
