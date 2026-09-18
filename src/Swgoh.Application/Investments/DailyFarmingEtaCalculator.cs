namespace Swgoh.Application.Investments;

internal static class DailyFarmingEtaCalculator
{
    private const int DedicatedCantinaEnergyCost = 16;

    private static readonly IReadOnlyDictionary<string, SignalDataRate> ConservativeSignalDataRates =
        new Dictionary<string, SignalDataRate>(StringComparer.Ordinal)
        {
            ["signal_data_fragmented"] = new(1.35m, "Sector 8 empirical conservative baseline"),
            ["signal_data_incomplete"] = new(0.90m, "Sector 8 empirical conservative baseline"),
            ["signal_data_flawed"] = new(0.65m, "Sector 8 empirical conservative baseline")
        };

    public static DailyFarmingEtaProjection Build(
        InvestmentFarmingPlan farmingPlan,
        DailyCrystalBudgetPlan crystalBudget,
        IReadOnlyCollection<DailyEnergyBaseline> baselines,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(farmingPlan);
        ArgumentNullException.ThrowIfNull(crystalBudget);
        ArgumentNullException.ThrowIfNull(baselines);

        int plannedCantinaEnergy = PlannedDailyEnergy(
            DailyFarmingChannel.CantinaEnergy,
            baselines,
            crystalBudget);
        DailyResourceEta[] resourceEtas = BuildResourceEtas(
            farmingPlan,
            plannedCantinaEnergy,
            generatedAtUtc);
        DailyTargetEta[] targetEtas = BuildTargetEtas(
            farmingPlan,
            resourceEtas,
            generatedAtUtc);

        return new DailyFarmingEtaProjection(resourceEtas, targetEtas);
    }

    private static DailyResourceEta[] BuildResourceEtas(
        InvestmentFarmingPlan farmingPlan,
        int plannedCantinaEnergy,
        DateTimeOffset generatedAtUtc)
    {
        if (!farmingPlan.HasInventorySnapshot || plannedCantinaEnergy <= 0)
        {
            return [];
        }

        decimal cumulativeEnergy = 0m;
        var result = new List<DailyResourceEta>();
        foreach (FarmingResourcePriority resource in farmingPlan.Resources
            .Where(resource => resource.Missing is > 0)
            .Where(resource => ConservativeSignalDataRates.ContainsKey(resource.ResourceId))
            .OrderBy(resource => resource.Rank))
        {
            long missing = resource.Missing!.Value;
            SignalDataRate rate = ConservativeSignalDataRates[resource.ResourceId];
            decimal expectedAttempts = missing / rate.ExpectedDropsPerAttempt;
            decimal expectedEnergy = expectedAttempts * DedicatedCantinaEnergyCost;
            cumulativeEnergy += expectedEnergy;
            int estimatedDays = Math.Max(
                1,
                (int)Math.Ceiling(cumulativeEnergy / plannedCantinaEnergy));
            decimal expectedDailyYield = Math.Round(
                plannedCantinaEnergy / (decimal)DedicatedCantinaEnergyCost * rate.ExpectedDropsPerAttempt,
                1);

            result.Add(new DailyResourceEta(
                resource.ResourceId,
                resource.ResourceName,
                missing,
                rate.ExpectedDropsPerAttempt,
                DedicatedCantinaEnergyCost,
                plannedCantinaEnergy,
                expectedDailyYield,
                estimatedDays,
                generatedAtUtc.AddDays(estimatedDays),
                rate.Basis));
        }

        return [.. result];
    }

    private static DailyTargetEta[] BuildTargetEtas(
        InvestmentFarmingPlan farmingPlan,
        IReadOnlyCollection<DailyResourceEta> resourceEtas,
        DateTimeOffset generatedAtUtc)
    {
        IReadOnlyDictionary<string, DailyResourceEta> etaByResource = resourceEtas
            .ToDictionary(eta => eta.ResourceId, StringComparer.Ordinal);

        return
        [
            .. farmingPlan.Targets.Select(target =>
                BuildTargetEta(farmingPlan, target, etaByResource, generatedAtUtc))
        ];
    }

    private static DailyTargetEta BuildTargetEta(
        InvestmentFarmingPlan farmingPlan,
        FarmingTargetPlan target,
        IReadOnlyDictionary<string, DailyResourceEta> etaByResource,
        DateTimeOffset generatedAtUtc)
    {
        FarmingResourcePriority[] blockers =
        [
            .. farmingPlan.Resources
                .Where(resource => resource.Missing is > 0)
                .Where(resource => resource.Targets.Any(dependency =>
                    string.Equals(
                        dependency.DefinitionId,
                        target.DefinitionId,
                        StringComparison.OrdinalIgnoreCase)))
        ];

        DailyResourceEta[] modeled =
        [
            .. blockers
                .Where(resource => etaByResource.ContainsKey(resource.ResourceId))
                .Select(resource => etaByResource[resource.ResourceId])
        ];
        int unknown = blockers.Length - modeled.Length;
        if (target.StarStepsRemaining > 0)
        {
            unknown++;
        }

        DateTimeOffset? knownBottleneck = modeled.Length == 0
            ? null
            : modeled.Max(eta => eta.EstimatedCompletionAtUtc);
        int? knownDays = modeled.Length == 0
            ? null
            : modeled.Max(eta => eta.EstimatedDays);
        bool readyNow = blockers.Length == 0
            && target.StarStepsRemaining == 0
            && farmingPlan.HasInventorySnapshot;
        bool fullEstimate = readyNow || (unknown == 0 && modeled.Length > 0);
        DateTimeOffset? completion = readyNow
            ? generatedAtUtc
            : fullEstimate
                ? knownBottleneck
                : null;
        int? estimatedDays = readyNow
            ? 0
            : fullEstimate
                ? knownDays
                : null;

        return new DailyTargetEta(
            target.DefinitionId,
            target.Name,
            target.ThumbnailName,
            readyNow,
            fullEstimate,
            estimatedDays,
            completion,
            modeled.Length,
            unknown,
            knownBottleneck,
            Summary(
                farmingPlan,
                target,
                readyNow,
                fullEstimate,
                estimatedDays,
                knownDays,
                modeled.Length,
                unknown));
    }

    private static string Summary(
        InvestmentFarmingPlan farmingPlan,
        FarmingTargetPlan target,
        bool readyNow,
        bool fullEstimate,
        int? estimatedDays,
        int? knownDays,
        int modeled,
        int unknown)
    {
        if (!farmingPlan.HasInventorySnapshot)
        {
            return "Actualiza el inventario para calcular una ETA desde déficits reales.";
        }

        if (readyNow)
        {
            return "Los recursos contabilizados del objetivo están cubiertos; puede ejecutarse ahora.";
        }

        if (fullEstimate && estimatedDays is int days)
        {
            return $"ETA conservadora: ~{days} día{(days == 1 ? string.Empty : "s")} con el presupuesto actual.";
        }

        if (modeled > 0 && knownDays is int partialDays)
        {
            return $"ETA parcial de Signal Data: ~{partialDays} día{(partialDays == 1 ? string.Empty : "s")}; quedan {unknown} bloqueo{(unknown == 1 ? string.Empty : "s")} sin ETA fiable.";
        }

        if (target.StarStepsRemaining > 0 && !target.RelicMaterialsTracked)
        {
            return "Sin ETA fiable: el objetivo depende de fragmentos de estrellas no expuestos por el proveedor.";
        }

        return $"Sin ETA completa: quedan {unknown} bloqueo{(unknown == 1 ? string.Empty : "s")} cuya cadencia no está modelada.";
    }

    private static int PlannedDailyEnergy(
        DailyFarmingChannel channel,
        IReadOnlyCollection<DailyEnergyBaseline> baselines,
        DailyCrystalBudgetPlan crystalBudget)
    {
        int baseline = baselines
            .FirstOrDefault(item => item.Channel == channel)
            ?.BaselineFreeEnergy ?? 0;
        int gained = crystalBudget.Refreshes
            .FirstOrDefault(refresh => refresh.Channel == channel)
            ?.EnergyGained ?? 0;
        return baseline + gained;
    }

    private sealed record SignalDataRate(
        decimal ExpectedDropsPerAttempt,
        string Basis);
}

internal sealed record DailyFarmingEtaProjection(
    IReadOnlyCollection<DailyResourceEta> ResourceEtas,
    IReadOnlyCollection<DailyTargetEta> TargetEtas);
