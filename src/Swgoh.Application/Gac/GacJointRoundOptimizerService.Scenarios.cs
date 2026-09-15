using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed partial class GacJointRoundOptimizerService
{
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

    private static GacPlannerState BuildHypotheticalState(
        GacPlannerState state,
        IReadOnlyCollection<GacSmartDefenseAssignment> assignments,
        IReadOnlyCollection<GacTeamPresetDetails> defensePresets)
    {
        Dictionary<Guid, GacTeamPresetDetails> presets = defensePresets.ToDictionary(item => item.Id);
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

    private static string FormatName(GacFormat format) => format == GacFormat.ThreeVsThree ? "3v3" : "5v5";

    private sealed record DefenseScenario(
        string Id,
        GacSmartDefenseService.SmartGeneration Generation);
}
