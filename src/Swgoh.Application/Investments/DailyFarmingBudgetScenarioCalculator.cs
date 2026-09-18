namespace Swgoh.Application.Investments;

internal static class DailyFarmingBudgetScenarioCalculator
{
    private static readonly int[] PresetBudgets = [0, 50, 150, 300];

    public static IReadOnlyCollection<DailyBudgetScenario> Build(
        InvestmentFarmingPlan farmingPlan,
        IReadOnlyCollection<DailyFarmingAction> actions,
        IReadOnlyCollection<DailyEnergyBaseline> baselines,
        int currentBudget,
        DateTimeOffset generatedAtUtc,
        IReadOnlyDictionary<string, decimal>? manualDailyRates = null)
    {
        ArgumentNullException.ThrowIfNull(farmingPlan);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(baselines);

        manualDailyRates ??= new Dictionary<string, decimal>(StringComparer.Ordinal);
        int normalizedCurrentBudget = Math.Clamp(currentBudget, 0, 5_000);
        int[] budgets =
        [
            .. PresetBudgets
                .Append(normalizedCurrentBudget)
                .Distinct()
                .Order()
        ];

        ScenarioSnapshot baseline = BuildSnapshot(
            farmingPlan,
            actions,
            baselines,
            0,
            generatedAtUtc,
            manualDailyRates);
        IReadOnlyDictionary<string, int?> baselineDaysByTarget = baseline.TargetEtas
            .ToDictionary(
                target => target.DefinitionId,
                target => target.FullEstimateAvailable ? target.EstimatedDays : null,
                StringComparer.OrdinalIgnoreCase);

        return
        [
            .. budgets.Select(budget =>
                BuildScenario(
                    farmingPlan,
                    actions,
                    baselines,
                    budget,
                    normalizedCurrentBudget,
                    generatedAtUtc,
                    manualDailyRates,
                    baseline.ModeledPortfolioDays,
                    baselineDaysByTarget))
        ];
    }

    private static DailyBudgetScenario BuildScenario(
        InvestmentFarmingPlan farmingPlan,
        IReadOnlyCollection<DailyFarmingAction> actions,
        IReadOnlyCollection<DailyEnergyBaseline> baselines,
        int budget,
        int currentBudget,
        DateTimeOffset generatedAtUtc,
        IReadOnlyDictionary<string, decimal> manualDailyRates,
        int? baselinePortfolioDays,
        IReadOnlyDictionary<string, int?> baselineDaysByTarget)
    {
        ScenarioSnapshot snapshot = BuildSnapshot(
            farmingPlan,
            actions,
            baselines,
            budget,
            generatedAtUtc,
            manualDailyRates);
        int? daysSavedVsF2P = baselinePortfolioDays is int baselineDays
            && snapshot.ModeledPortfolioDays is int scenarioDays
                ? Math.Max(0, baselineDays - scenarioDays)
                : null;

        DailyBudgetScenarioTarget[] targets =
        [
            .. snapshot.TargetEtas
                .Select(target =>
                {
                    int? baselineDays = baselineDaysByTarget.GetValueOrDefault(target.DefinitionId);
                    int? daysSaved = baselineDays is int f2pDays
                        && target.FullEstimateAvailable
                        && target.EstimatedDays is int targetDays
                            ? Math.Max(0, f2pDays - targetDays)
                            : null;

                    return new DailyBudgetScenarioTarget(
                        target.DefinitionId,
                        target.Name,
                        target.FullEstimateAvailable,
                        target.EstimatedDays,
                        daysSaved);
                })
        ];

        return new DailyBudgetScenario(
            snapshot.CrystalBudget.DailyCrystalBudget,
            snapshot.CrystalBudget.ProfileLabel,
            budget == currentBudget,
            snapshot.CrystalBudget.CrystalsSpent,
            snapshot.CrystalBudget.CrystalsUnspent,
            snapshot.CrystalBudget.RefreshCount,
            snapshot.CrystalBudget.EnergyGained,
            snapshot.TargetEtas.Count(target => target.FullEstimateAvailable),
            snapshot.TargetEtas.Count(target => target.ReadyNow),
            snapshot.ModeledPortfolioDays,
            snapshot.ModeledPortfolioDays is int modeledDays
                ? generatedAtUtc.AddDays(modeledDays)
                : null,
            daysSavedVsF2P,
            targets);
    }

    private static ScenarioSnapshot BuildSnapshot(
        InvestmentFarmingPlan farmingPlan,
        IReadOnlyCollection<DailyFarmingAction> actions,
        IReadOnlyCollection<DailyEnergyBaseline> baselines,
        int budget,
        DateTimeOffset generatedAtUtc,
        IReadOnlyDictionary<string, decimal> manualDailyRates)
    {
        DailyCrystalBudgetPlan crystalBudget = DailyEnergyRefreshPlanner.Build(
            budget,
            actions,
            baselines);
        DailyFarmingEtaProjection eta = DailyFarmingEtaCalculator.Build(
            farmingPlan,
            crystalBudget,
            baselines,
            generatedAtUtc,
            manualDailyRates);
        int? modeledPortfolioDays = ModeledPortfolioDays(eta.TargetEtas);

        return new ScenarioSnapshot(
            crystalBudget,
            eta.TargetEtas,
            modeledPortfolioDays);
    }

    private static int? ModeledPortfolioDays(IReadOnlyCollection<DailyTargetEta> targetEtas)
    {
        int[] modeledDays =
        [
            .. targetEtas
                .Where(target => target.FullEstimateAvailable)
                .Select(target => target.EstimatedDays ?? 0)
        ];
        return modeledDays.Length == 0 ? null : modeledDays.Max();
    }

    private sealed record ScenarioSnapshot(
        DailyCrystalBudgetPlan CrystalBudget,
        IReadOnlyCollection<DailyTargetEta> TargetEtas,
        int? ModeledPortfolioDays);
}
