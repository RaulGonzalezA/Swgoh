namespace Swgoh.Application.Investments;

internal static class DailyFarmingEtaCalculator
{
    private static readonly IReadOnlyDictionary<string, ResourceFarmRate> ConservativeFarmRates =
        new Dictionary<string, ResourceFarmRate>(StringComparer.Ordinal)
        {
            ["signal_data_fragmented"] = new(
                DailyFarmingChannel.CantinaEnergy,
                1.35m,
                16,
                "Cantina 8-C · conservative empirical baseline"),
            ["signal_data_incomplete"] = new(
                DailyFarmingChannel.CantinaEnergy,
                0.90m,
                16,
                "Cantina 8-F · conservative empirical baseline"),
            ["signal_data_flawed"] = new(
                DailyFarmingChannel.CantinaEnergy,
                0.65m,
                16,
                "Cantina 8-G · conservative empirical baseline"),
            ["carbonite_circuit_board"] = new(
                DailyFarmingChannel.NormalEnergy,
                0.70m,
                6,
                "Light Side 1-C · conservative feedstock conversion baseline"),
            ["bronzium_wiring"] = new(
                DailyFarmingChannel.NormalEnergy,
                0.20m,
                10,
                "Light Side 7-B · conservative Mk 5 Fabritech conversion baseline")
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

        IReadOnlyDictionary<DailyFarmingChannel, int> plannedEnergyByChannel =
            Enum.GetValues<DailyFarmingChannel>()
                .ToDictionary(
                    channel => channel,
                    channel => PlannedDailyEnergy(channel, baselines, crystalBudget));
        DailyResourceEta[] resourceEtas = BuildResourceEtas(
            farmingPlan,
            plannedEnergyByChannel,
            generatedAtUtc);
        DailyTargetEta[] targetEtas = BuildTargetEtas(
            farmingPlan,
            resourceEtas,
            generatedAtUtc);

        return new DailyFarmingEtaProjection(resourceEtas, targetEtas);
    }

    private static DailyResourceEta[] BuildResourceEtas(
        InvestmentFarmingPlan farmingPlan,
        IReadOnlyDictionary<DailyFarmingChannel, int> plannedEnergyByChannel,
        DateTimeOffset generatedAtUtc)
    {
        if (!farmingPlan.HasInventorySnapshot)
        {
            return [];
        }

        var cumulativeEnergyByChannel = new Dictionary<DailyFarmingChannel, decimal>();
        var result = new List<DailyResourceEta>();
        foreach (FarmingResourcePriority resource in farmingPlan.Resources
            .Where(resource => resource.Missing is > 0)
            .Where(resource => ConservativeFarmRates.ContainsKey(resource.ResourceId))
            .OrderBy(resource => resource.Rank))
        {
            long missing = resource.Missing!.Value;
            ResourceFarmRate rate = ConservativeFarmRates[resource.ResourceId];
            int plannedDailyEnergy = plannedEnergyByChannel.GetValueOrDefault(rate.Channel);
            if (plannedDailyEnergy <= 0)
            {
                continue;
            }

            decimal expectedAttempts = missing / rate.ExpectedDropsPerAttempt;
            decimal expectedEnergy = expectedAttempts * rate.EnergyCostPerAttempt;
            decimal cumulativeEnergy = cumulativeEnergyByChannel.GetValueOrDefault(rate.Channel)
                + expectedEnergy;
            cumulativeEnergyByChannel[rate.Channel] = cumulativeEnergy;

            int estimatedDays = Math.Max(
                1,
                (int)Math.Ceiling(cumulativeEnergy / plannedDailyEnergy));
            decimal expectedDailyYield = Math.Round(
                plannedDailyEnergy / (decimal)rate.EnergyCostPerAttempt * rate.ExpectedDropsPerAttempt,
                1);

            result.Add(new DailyResourceEta(
                resource.ResourceId,
                resource.ResourceName,
                missing,
                rate.ExpectedDropsPerAttempt,
                rate.EnergyCostPerAttempt,
                plannedDailyEnergy,
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

    private sealed record ResourceFarmRate(
        DailyFarmingChannel Channel,
        decimal ExpectedDropsPerAttempt,
        int EnergyCostPerAttempt,
        string Basis);
}

internal sealed record DailyFarmingEtaProjection(
    IReadOnlyCollection<DailyResourceEta> ResourceEtas,
    IReadOnlyCollection<DailyTargetEta> TargetEtas);
