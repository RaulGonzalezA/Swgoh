using System.Diagnostics;

using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Gac;

public enum GacJointRoundOptimizationMode
{
    Balanced = 0,
    DefenseFirst = 1,
    OffenseFirst = 2,
    MaxBanners = 3
}

public interface IGacJointRoundOptimizerService
{
    Task<GacJointRoundOptimizationLookup> OptimizeCurrentAsync(
        long allyCode,
        GacJointRoundOptimizationMode mode,
        bool apply,
        CancellationToken cancellationToken = default);
}

public sealed record GacJointRoundScenario(
    string ScenarioId,
    decimal JointScore,
    decimal DefenseScore,
    decimal DefenseCompletionRate,
    decimal AttackCoverageRate,
    decimal AttackScore,
    decimal? KnownAverageBanners,
    decimal OffensePreservationScore,
    decimal AverageDefenseOpportunityCost,
    int RecommendedAttacks,
    int TargetDefenses,
    int HistoricalMatches,
    bool AttackSearchLimitReached,
    IReadOnlyCollection<GacSmartDefenseAssignment> DefenseAssignments,
    IReadOnlyCollection<GacAttackOptimizationRecommendation> AttackRecommendations,
    IReadOnlyCollection<Guid> UncoveredDefenseIds,
    IReadOnlyCollection<string> Warnings);

public sealed record GacJointRoundOptimizationResult(
    GacFormat Format,
    GacJointRoundOptimizationMode Mode,
    bool Applied,
    int ScenariosEvaluated,
    GacJointRoundScenario Selected,
    IReadOnlyCollection<GacJointRoundScenario> Alternatives,
    DateTimeOffset? PlanUpdatedAtUtc,
    IReadOnlyCollection<string> Warnings);

public sealed record GacJointRoundOptimizationLookup(
    CurrentGacOpponentStatus Status,
    string? Message,
    GacPlannerState? State,
    GacJointRoundOptimizationResult? Optimization)
{
    public bool IsAvailable =>
        Status == CurrentGacOpponentStatus.Found &&
        State is not null &&
        Optimization is not null;
}

internal sealed partial class GacJointRoundOptimizerService(
    GacDefenseStrategyService strategyService,
    IGacPlannerService plannerService,
    ICurrentGacScoutingService scoutingService,
    IGacPersonalLearningService personalLearningService,
    IPlayerProfileService playerProfileService,
    GacRosterDefenseCandidateService rosterCandidateService,
    GacOptimizationCoordinator? optimizationCoordinator = null) : IGacJointRoundOptimizerService
{
    private const int HistoryRoundLimit = 30;
    private const int CandidatesPerSlot = 5;
    private const int PairSlotsLimit = 4;
    private const int PairCandidatesPerSlot = 2;
    private const int MaxDefenseScenarios = 80;
    private const int AlternativeLimit = 3;
    private static readonly TimeSpan MaxOptimizationDuration = TimeSpan.FromSeconds(10);

    public async Task<GacJointRoundOptimizationLookup> OptimizeCurrentAsync(
        long allyCode,
        GacJointRoundOptimizationMode mode,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported joint round optimization mode.");
        }

        using IDisposable? optimizationLease = optimizationCoordinator is null
            ? null
            : await optimizationCoordinator.AcquireAsync(cancellationToken).ConfigureAwait(false);

        GacPlannerLookup plannerLookup = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (!plannerLookup.IsAvailable || plannerLookup.State is null)
        {
            return new GacJointRoundOptimizationLookup(
                plannerLookup.Status,
                plannerLookup.Message,
                plannerLookup.State,
                null);
        }

        GacPlannerState state = plannerLookup.State;
        GacBoardLayout boardLayout = GacBoardLayouts.Get(state.Plan.League, state.Plan.Format);
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
        Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(allyCode, cancellationToken);
        Task<PlayerProfile?> opponentTask = playerProfileService.GetAsync(
            state.Plan.OpponentAllyCode,
            cancellationToken);

        await Task.WhenAll(strategyTask, scoutingTask, personalTask, playerTask, opponentTask)
            .ConfigureAwait(false);

        GacDefenseStrategySnapshot strategy = await strategyTask.ConfigureAwait(false);
        CurrentGacScoutingResult scouting = await scoutingTask.ConfigureAwait(false);
        IReadOnlyCollection<GacPersonalMatchupStatistics> personal = await personalTask.ConfigureAwait(false);
        PlayerProfile? player = await playerTask.ConfigureAwait(false);
        PlayerProfile? opponent = await opponentTask.ConfigureAwait(false);

        HashSet<string> alreadyConsumedUnits = state.Plan.Attacks
            .Where(attack => attack.Status is GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed)
            .SelectMany(attack => attack.Team.Squad.AllUnits)
            .Select(unit => unit.DefinitionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        GacRosterDefenseCandidateSet rosterCandidates = await rosterCandidateService.BuildAsync(
            allyCode,
            state.Plan.Format,
            strategy.Profile,
            state.Presets,
            scouting.BattlePlan,
            alreadyConsumedUnits,
            cancellationToken).ConfigureAwait(false);
        GacTeamPresetDetails[] defenseEligiblePresets =
        [
            .. state.Presets.Where(preset =>
                !preset.Squad.AllUnits.Any(unit => alreadyConsumedUnits.Contains(unit.DefinitionId))),
            .. rosterCandidates.Candidates
        ];

        IReadOnlyCollection<DefenseScenario> defenseScenarios = BuildDefenseScenarios(
            strategy.Profile,
            defenseEligiblePresets,
            scouting,
            personal,
            state.PlayerDatacrons,
            cancellationToken);
        if (defenseScenarios.Count == 0)
        {
            throw new InvalidOperationException("No valid defense scenarios could be generated for the current round.");
        }

        GacTacticalOptimizationContext tacticalContext = GacTacticalOptimizationContext.From(player, opponent);
        GacPersonalLearningContext personalContext = GacPersonalLearningContext.From(personal);
        var evaluated = new List<GacJointRoundScenario>(defenseScenarios.Count);
        long startedAt = Stopwatch.GetTimestamp();
        bool optimizationBudgetReached = false;

        foreach (DefenseScenario defenseScenario in defenseScenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (evaluated.Count > 0 && Stopwatch.GetElapsedTime(startedAt) >= MaxOptimizationDuration)
            {
                optimizationBudgetReached = true;
                break;
            }

            GacPlannerState hypothetical = BuildHypotheticalState(
                state,
                defenseScenario.Generation.Assignments,
                defenseEligiblePresets);
            GacAttackOptimizationResult attacks = GacAttackPlanOptimizerService.Optimize(
                hypothetical,
                GacAttackOptimizationMode.RebuildPlanned,
                tacticalContext,
                personalContext,
                cancellationToken);
            evaluated.Add(EvaluateScenario(
                defenseScenario.Id,
                boardLayout.TotalDefenseSlots,
                defenseScenario.Generation,
                attacks,
                mode));
        }

        GacJointRoundScenario selected = evaluated
            .OrderByDescending(item => item.JointScore)
            .ThenByDescending(item => item.AttackCoverageRate)
            .ThenByDescending(item => item.AttackScore)
            .ThenByDescending(item => item.DefenseScore)
            .First();
        GacJointRoundScenario[] alternatives =
        [
            .. evaluated
                .Where(item => item.ScenarioId != selected.ScenarioId)
                .OrderByDescending(item => item.JointScore)
                .ThenByDescending(item => item.AttackCoverageRate)
                .Take(AlternativeLimit)
        ];

        var warnings = new List<string>(rosterCandidates.Warnings);
        if (selected.DefenseCompletionRate < 100m)
        {
            warnings.Add(
                $"La defensa propuesta cubre {selected.DefenseAssignments.Count}/{boardLayout.TotalDefenseSlots} huecos requeridos para {state.Plan.League} {FormatName(state.Plan.Format)}.");
        }
        if (selected.AttackCoverageRate < 100m && selected.TargetDefenses > 0)
        {
            warnings.Add(
                $"El mejor reparto solo cubre {selected.RecommendedAttacks}/{selected.TargetDefenses} defensas visibles; revisa presets o reservas.");
        }
        if (evaluated.Any(item => item.AttackSearchLimitReached))
        {
            warnings.Add("Algún escenario alcanzó el límite interno de búsqueda del optimizador de ataques.");
        }
        if (optimizationBudgetReached)
        {
            warnings.Add(
                $"El optimizador conjunto agotó su presupuesto de {MaxOptimizationDuration.TotalSeconds:0} s tras evaluar {evaluated.Count} escenario(s); se conserva el mejor resultado encontrado.");
        }
        if (alreadyConsumedUnits.Count > 0)
        {
            warnings.Add("Se excluyeron de la defensa los equipos que reutilizan unidades ya consumidas en ataques completados.");
        }

        if (!apply)
        {
            GacJointRoundOptimizationResult preview = new(
                state.Plan.Format,
                mode,
                Applied: false,
                evaluated.Count,
                selected,
                alternatives,
                state.Plan.UpdatedAtUtc,
                warnings);
            return new GacJointRoundOptimizationLookup(
                CurrentGacOpponentStatus.Found,
                null,
                state,
                preview);
        }

        GacGeneratedPresetMaterialization materialization = await GacRosterDefenseCandidateService.MaterializeAsync(
            plannerService,
            allyCode,
            state.Plan.Format,
            selected.DefenseAssignments,
            rosterCandidates,
            cancellationToken).ConfigureAwait(false);
        GacSmartDefenseAssignment[] appliedDefenseAssignments =
        [
            .. selected.DefenseAssignments.Select(item =>
                GacRosterDefenseCandidateService.Remap(item, materialization.IdMap))
        ];
        GacJointRoundScenario appliedSelected = selected with
        {
            DefenseAssignments = appliedDefenseAssignments
        };

        try
        {
            GacPlannerLookup saved = await ApplyAsync(
                allyCode,
                state,
                appliedSelected,
                mode,
                cancellationToken).ConfigureAwait(false);
            if (!saved.IsAvailable || saved.State is null)
            {
                throw new InvalidOperationException(saved.Message ?? "The joint GAC round plan could not be applied.");
            }

            GacJointRoundOptimizationResult applied = new(
                state.Plan.Format,
                mode,
                Applied: true,
                evaluated.Count,
                appliedSelected,
                alternatives,
                saved.State.Plan.UpdatedAtUtc,
                warnings);
            return new GacJointRoundOptimizationLookup(
                saved.Status,
                saved.Message,
                saved.State,
                applied);
        }
        catch
        {
            await GacRosterDefenseCandidateService.RollbackMaterializationAsync(
                plannerService,
                allyCode,
                materialization.CreatedPresetIds).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<GacPlannerLookup> ApplyAsync(
        long allyCode,
        GacPlannerState state,
        GacJointRoundScenario selected,
        GacJointRoundOptimizationMode mode,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SaveGacOwnDefenseAssignment> ownDefenses =
        [
            .. selected.DefenseAssignments.Select(item =>
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

        var attacks = new List<SaveGacAttackAssignment>();
        foreach (GacAttackAssignmentDetails existing in state.Plan.Attacks.Where(attack =>
                     attack.Status != GacAttackPlanStatus.Planned))
        {
            attacks.Add(new SaveGacAttackAssignment(
                existing.Id,
                existing.DefenseId,
                existing.Team.Id,
                existing.Attempt,
                existing.Status,
                existing.Notes));
        }

        foreach (GacAttackOptimizationRecommendation recommendation in selected.AttackRecommendations)
        {
            int attempt = attacks
                .Where(attack => attack.DefenseId == recommendation.DefenseId)
                .Select(attack => attack.Attempt)
                .DefaultIfEmpty(0)
                .Max() + 1;
            string notes =
                $"Optimizador conjunto {mode}: round score {selected.JointScore:0.#}; " +
                $"counter {recommendation.Score:0.#}; {recommendation.Evidence}; " +
                $"coste estratégico {recommendation.StrategicCost:0.#}.";
            attacks.Add(new SaveGacAttackAssignment(
                null,
                recommendation.DefenseId,
                recommendation.TeamPresetId,
                attempt,
                GacAttackPlanStatus.Planned,
                notes));
        }

        return await plannerService.SaveCurrentAsync(
            allyCode,
            new SaveCurrentGacRoundPlan(ownDefenses, visibleDefenses, attacks, state.Plan.Version),
            cancellationToken).ConfigureAwait(false);
    }
}
