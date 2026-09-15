using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class RosterAwareGacSmartDefenseService(
    GacDefenseStrategyService strategyService,
    IGacPlannerService plannerService,
    ICurrentGacScoutingService scoutingService,
    IGacPersonalLearningService personalLearningService,
    IGacRosterDefenseCandidateProvider rosterCandidateProvider) : IGacSmartDefenseService
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

        await Task.WhenAll(strategyTask, scoutingTask, personalTask).ConfigureAwait(false);
        GacDefenseStrategySnapshot strategy = await strategyTask.ConfigureAwait(false);
        CurrentGacScoutingResult scouting = await scoutingTask.ConfigureAwait(false);
        IReadOnlyCollection<GacPersonalMatchupStatistics> personal = await personalTask.ConfigureAwait(false);

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
            strategy.Profile,
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
            strategy.Profile,
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
        GacSmartDefenseService.SmartGeneration appliedGeneration = generation with
        {
            Assignments = appliedAssignments
        };

        try
        {
            GacPlannerLookup saved = await SaveAsync(
                allyCode,
                state,
                appliedAssignments,
                cancellationToken).ConfigureAwait(false);
            if (!saved.IsAvailable || saved.State is null)
            {
                throw new InvalidOperationException(saved.Message ?? "The smart defense could not be applied.");
            }

            return ToResult(
                state.Plan.Format,
                true,
                appliedGeneration,
                saved.State.Plan.UpdatedAtUtc,
                scouting);
        }
        catch
        {
            await RollbackAsync(allyCode, materialization).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<GacPlannerLookup> SaveAsync(
        long allyCode,
        GacPlannerState state,
        IReadOnlyCollection<GacSmartDefenseAssignment> assignments,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SaveGacOwnDefenseAssignment> ownDefenses =
        [
            .. assignments.Select(item =>
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

        return await plannerService.SaveCurrentAsync(
            allyCode,
            new SaveCurrentGacRoundPlan(ownDefenses, visibleDefenses, attacks, state.Plan.Version),
            cancellationToken).ConfigureAwait(false);
    }

    private Task RollbackAsync(
        long allyCode,
        GacGeneratedPresetMaterialization materialization) =>
        GacRosterDefenseCandidateService.RollbackMaterializationAsync(
            plannerService,
            allyCode,
            materialization.CreatedPresetIds);

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
