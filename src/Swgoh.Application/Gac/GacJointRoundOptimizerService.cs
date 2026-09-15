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

internal sealed class GacJointRoundOptimizerService(
    GacDefenseStrategyService strategyService,
    IGacPlannerService plannerService,
    ICurrentGacScoutingService scoutingService,
    IGacPersonalLearningService personalLearningService,
    IPlayerProfileService playerProfileService) : IGacJointRoundOptimizerService
{
    private const int HistoryRoundLimit = 30;
    private const int CandidatesPerSlot = 5;
    private const int PairSlotsLimit = 4;
    private const int PairCandidatesPerSlot = 2;
    private const int MaxDefenseScenarios = 80;
    private const int AlternativeLimit = 3;

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
        GacTeamPresetDetails[] defenseEligiblePresets =
        [
            .. state.Presets.Where(preset =>
                !preset.Squad.AllUnits.Any(unit => alreadyConsumedUnits.Contains(unit.DefinitionId)))
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

        foreach (DefenseScenario defenseScenario in defenseScenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GacPlannerState hypothetical = BuildHypotheticalState(state, defenseScenario.Generation.Assignments);
            GacAttackOptimizationResult attacks = GacAttackPlanOptimizerService.Optimize(
                hypothetical,
                GacAttackOptimizationMode.RebuildPlanned,
                tacticalContext,
                personalContext);
            evaluated.Add(EvaluateScenario(
                defenseScenario.Id,
                strategy.Profile.Slots.Count,
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

        var warnings = new List<string>();
        if (selected.AttackCoverageRate < 100m && selected.TargetDefenses > 0)
        {
            warnings.Add(
                $"El mejor reparto solo cubre {selected.RecommendedAttacks}/{selected.TargetDefenses} defensas visibles; revisa presets o reservas.");
        }
        if (evaluated.Any(item => item.AttackSearchLimitReached))
        {
            warnings.Add("Algún escenario alcanzó el límite interno de búsqueda del optimizador de ataques.");
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

        GacPlannerLookup saved = await ApplyAsync(
            allyCode,
            state,
            selected,
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
            selected,
            alternatives,
            saved.State.Plan.UpdatedAtUtc,
            warnings);
        return new GacJointRoundOptimizationLookup(
            saved.Status,
            saved.Message,
            saved.State,
            applied);
    }

    internal static GacJointRoundScenario EvaluateScenario(
        string scenarioId,
        int requestedDefenseSlots,
        GacSmartDefenseService.SmartGeneration defense,
        GacAttackOptimizationResult attacks,
        GacJointRoundOptimizationMode mode)
    {
        decimal defenseAverage = defense.Assignments.Count == 0
            ? 0m
            : Math.Round(defense.Assignments.Average(item => item.Score), 1);
        decimal defenseCompletion = requestedDefenseSlots <= 0
            ? 100m
            : Math.Round(defense.Assignments.Count * 100m / requestedDefenseSlots, 1);
        decimal defenseScore = Math.Round((defenseAverage * 0.8m) + (defenseCompletion * 0.2m), 1);
        decimal attackCoverage = attacks.TargetDefenses <= 0
            ? 100m
            : Math.Round(attacks.RecommendedAttacks * 100m / attacks.TargetDefenses, 1);
        decimal attackScore = attacks.RecommendedAttacks == 0 && attacks.TargetDefenses > 0
            ? 0m
            : attacks.AverageScore;
        decimal bannerScore = attacks.KnownAverageBanners is decimal banners
            ? Math.Round(Math.Clamp((banners - 40m) / 30m * 100m, 0m, 100m), 1)
            : attackScore;
        decimal averageOpportunityCost = defense.Assignments.Count == 0
            ? 0m
            : Math.Round(defense.Assignments.Average(item => item.OffensiveOpportunityCost), 1);
        decimal offensePreservation = Math.Round(
            Math.Clamp(100m - averageOpportunityCost * 4m, 0m, 100m),
            1);

        (decimal Defense, decimal Coverage, decimal Attack, decimal Banners, decimal Preservation) weights =
            Weights(mode);
        decimal jointScore = Math.Round(Math.Clamp(
            defenseScore * weights.Defense +
            attackCoverage * weights.Coverage +
            attackScore * weights.Attack +
            bannerScore * weights.Banners +
            offensePreservation * weights.Preservation -
            (attacks.SearchLimitReached ? 2m : 0m),
            0m,
            100m), 1);

        return new GacJointRoundScenario(
            scenarioId,
            jointScore,
            defenseScore,
            defenseCompletion,
            attackCoverage,
            attackScore,
            attacks.KnownAverageBanners,
            offensePreservation,
            averageOpportunityCost,
            attacks.RecommendedAttacks,
            attacks.TargetDefenses,
            attacks.HistoricalMatches,
            attacks.SearchLimitReached,
            defense.Assignments,
            attacks.Recommendations,
            attacks.UncoveredDefenseIds,
            defense.Warnings);
    }

    private static IReadOnlyCollection<DefenseScenario> BuildDefenseScenarios(
        GacDefenseStrategyProfile profile,
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        CurrentGacScoutingResult scouting,
        IReadOnlyCollection<GacPersonalMatchupStatistics> personal,
        IReadOnlyCollection<GacPlannerDatacronDetails> datacrons,
        CancellationToken cancellationToken)
    {
        var profiles = new List<(string Id, GacDefenseStrategyProfile Profile)>
        {
            ("smart-baseline", profile)
        };
        GacDefenseTemplateSlot[] autoSlots =
        [
            .. profile.Slots
                .Where(slot => slot.PinnedTeamPresetId is null)
                .OrderBy(slot => slot.Position)
        ];
        HashSet<Guid> reserved = profile.ReservedAttackPresetIds.ToHashSet();
        HashSet<Guid> manuallyPinned = profile.Slots
            .Where(slot => slot.PinnedTeamPresetId is not null)
            .Select(slot => slot.PinnedTeamPresetId!.Value)
            .ToHashSet();

        var candidatesBySlot = new Dictionary<int, GacTeamPresetDetails[]>();
        foreach (GacDefenseTemplateSlot slot in autoSlots)
        {
            GacTeamPresetDetails[] candidates =
            [
                .. presets
                    .Where(preset => !reserved.Contains(preset.Id))
                    .Where(preset => !manuallyPinned.Contains(preset.Id))
                    .Where(preset => preset.Squad.IsFleet == IsFleetZone(slot.Zone))
                    .OrderBy(PresetUsePriority)
                    .ThenByDescending(TeamPower)
                    .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(CandidatesPerSlot)
            ];
            candidatesBySlot[slot.Position] = candidates;
            foreach (GacTeamPresetDetails candidate in candidates)
            {
                profiles.Add((
                    $"slot-{slot.Position}-{candidate.Id:N}",
                    Pin(profile, (slot.Position, candidate.Id))));
            }
        }

        GacDefenseTemplateSlot[] pairSlots = [.. autoSlots.Take(PairSlotsLimit)];
        for (int left = 0; left < pairSlots.Length; left++)
        {
            for (int right = left + 1; right < pairSlots.Length; right++)
            {
                GacDefenseTemplateSlot firstSlot = pairSlots[left];
                GacDefenseTemplateSlot secondSlot = pairSlots[right];
                foreach (GacTeamPresetDetails first in candidatesBySlot[firstSlot.Position].Take(PairCandidatesPerSlot))
                {
                    foreach (GacTeamPresetDetails second in candidatesBySlot[secondSlot.Position].Take(PairCandidatesPerSlot))
                    {
                        if (first.Id == second.Id || SharesUnits(first, second))
                        {
                            continue;
                        }

                        profiles.Add((
                            $"pair-{firstSlot.Position}-{first.Id:N}-{secondSlot.Position}-{second.Id:N}",
                            Pin(
                                profile,
                                (firstSlot.Position, first.Id),
                                (secondSlot.Position, second.Id))));
                    }
                }
            }
        }

        var scenarios = new Dictionary<string, DefenseScenario>(StringComparer.Ordinal);
        foreach ((string id, GacDefenseStrategyProfile variant) in profiles.Take(MaxDefenseScenarios))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GacSmartDefenseService.SmartGeneration generation = GacSmartDefenseService.Generate(
                variant,
                presets,
                scouting,
                personal,
                datacrons);
            string key = ScenarioKey(generation.Assignments);
            if (scenarios.TryGetValue(key, out DefenseScenario? current))
            {
                decimal currentScore = current.Generation.Assignments.Sum(item => item.Score);
                decimal candidateScore = generation.Assignments.Sum(item => item.Score);
                if (candidateScore > currentScore)
                {
                    scenarios[key] = new DefenseScenario(id, generation);
                }
            }
            else
            {
                scenarios.Add(key, new DefenseScenario(id, generation));
            }
        }

        return [.. scenarios.Values];
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
            new SaveCurrentGacRoundPlan(ownDefenses, visibleDefenses, attacks),
            cancellationToken).ConfigureAwait(false);
    }

    private static GacPlannerState BuildHypotheticalState(
        GacPlannerState state,
        IReadOnlyCollection<GacSmartDefenseAssignment> assignments)
    {
        Dictionary<Guid, GacTeamPresetDetails> presets = state.Presets.ToDictionary(item => item.Id);
        GacOwnDefenseAssignmentDetails[] ownDefenses =
        [
            .. assignments
                .Where(item => presets.ContainsKey(item.TeamPresetId))
                .Select(item => new GacOwnDefenseAssignmentDetails(
                    item.TeamPresetId,
                    item.Zone,
                    presets[item.TeamPresetId]))
        ];
        GacRoundPlanDetails hypotheticalPlan = state.Plan with { OwnDefenses = ownDefenses };
        return state with { Plan = hypotheticalPlan };
    }

    private static GacDefenseStrategyProfile Pin(
        GacDefenseStrategyProfile profile,
        params (int Position, Guid PresetId)[] pins)
    {
        Dictionary<int, Guid> byPosition = pins.ToDictionary(item => item.Position, item => item.PresetId);
        GacDefenseTemplateSlot[] slots =
        [
            .. profile.Slots.Select(slot => byPosition.TryGetValue(slot.Position, out Guid presetId)
                ? slot with { PinnedTeamPresetId = presetId }
                : slot)
        ];
        return profile with { Slots = slots };
    }

    private static string ScenarioKey(IReadOnlyCollection<GacSmartDefenseAssignment> assignments) =>
        string.Join(
            '|',
            assignments
                .OrderBy(item => item.Position)
                .Select(item => $"{item.Position}:{item.TeamPresetId:N}"));

    private static bool IsFleetZone(string zone) =>
        zone.Contains("flota", StringComparison.OrdinalIgnoreCase) ||
        zone.Contains("fleet", StringComparison.OrdinalIgnoreCase);

    private static bool SharesUnits(GacTeamPresetDetails left, GacTeamPresetDetails right)
    {
        HashSet<string> leftUnits = left.Squad.AllUnits
            .Select(unit => unit.DefinitionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return right.Squad.AllUnits.Any(unit => leftUnits.Contains(unit.DefinitionId));
    }

    private static int PresetUsePriority(GacTeamPresetDetails preset) => preset.Use switch
    {
        GacPlannerTeamUse.Defense => 0,
        GacPlannerTeamUse.Flexible => 1,
        _ => 2
    };

    private static long TeamPower(GacTeamPresetDetails preset) =>
        preset.Squad.AllUnits.Sum(unit => unit.GalacticPower ?? 0L);

    private static (decimal Defense, decimal Coverage, decimal Attack, decimal Banners, decimal Preservation) Weights(
        GacJointRoundOptimizationMode mode) => mode switch
        {
            GacJointRoundOptimizationMode.DefenseFirst => (0.52m, 0.18m, 0.15m, 0.05m, 0.10m),
            GacJointRoundOptimizationMode.OffenseFirst => (0.18m, 0.32m, 0.30m, 0.08m, 0.12m),
            GacJointRoundOptimizationMode.MaxBanners => (0.18m, 0.25m, 0.18m, 0.32m, 0.07m),
            _ => (0.32m, 0.28m, 0.25m, 0.08m, 0.07m)
        };

    private sealed record DefenseScenario(
        string Id,
        GacSmartDefenseService.SmartGeneration Generation);
}
