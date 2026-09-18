using Swgoh.Application.Abstractions;

namespace Swgoh.Application.Investments;

public interface IInvestmentDailyFarmingPlanService
{
    Task<InvestmentDailyFarmingPlan> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default);

    Task<InvestmentDailyFarmingPlan> GetAsync(
        long allyCode,
        int dailyCrystalBudget,
        CancellationToken cancellationToken = default);
}

internal sealed class InvestmentDailyFarmingPlanService(
    IInvestmentFarmingPlanService farmingPlanService,
    IClock clock) : IInvestmentDailyFarmingPlanService
{
    private const int NormalEnergyPerDay = 375;
    private const int FleetEnergyPerDay = 285;
    private const int CantinaEnergyPerDay = 165;

    public Task<InvestmentDailyFarmingPlan> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default) =>
        GetAsync(allyCode, 0, cancellationToken);

    public async Task<InvestmentDailyFarmingPlan> GetAsync(
        long allyCode,
        int dailyCrystalBudget,
        CancellationToken cancellationToken = default)
    {
        InvestmentFarmingPlan farmingPlan = await farmingPlanService
            .GetAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);

        FarmingResourcePriority[] pendingResources =
        [
            .. farmingPlan.Resources.Where(resource =>
                !farmingPlan.HasInventorySnapshot || resource.Missing is > 0)
        ];

        var actions = pendingResources
            .Where(resource => resource.ResourceId is not (
                "signal_data_fragmented"
                or "signal_data_incomplete"
                or "signal_data_flawed"))
            .Select(BuildResourceAction)
            .ToList();
        actions.AddRange(SignalDataCantinaRouter.Build(pendingResources, CantinaEnergyPerDay));

        if (farmingPlan.ActiveTargetCount > 0)
        {
            actions.Add(BuildFleetGuardrailAction(farmingPlan));
        }

        DailyFarmingAction[] rankedActions =
        [
            .. actions
                .OrderBy(action => PriorityOrder(action.Priority))
                .ThenBy(action => action.Precision)
                .ThenByDescending(action => action.SharedBottleneck)
                .ThenByDescending(action => action.AffectedTargetCount)
                .ThenBy(action => ChannelOrder(action.Channel))
                .Select((action, index) => action with { Rank = index + 1 })
        ];

        IReadOnlyCollection<DailyEnergyBaseline> baselines = EnergyBaselines();
        DailyCrystalBudgetPlan crystalBudget = DailyEnergyRefreshPlanner.Build(
            dailyCrystalBudget,
            rankedActions,
            baselines);
        DateTimeOffset generatedAtUtc = clock.UtcNow;
        DailyFarmingEtaProjection eta = DailyFarmingEtaCalculator.Build(
            farmingPlan,
            crystalBudget,
            baselines,
            generatedAtUtc);
        IReadOnlyCollection<DailyBudgetScenario> budgetScenarios =
            DailyFarmingBudgetScenarioCalculator.Build(
                farmingPlan,
                rankedActions,
                baselines,
                dailyCrystalBudget,
                generatedAtUtc);
        string summary = BuildSummary(farmingPlan, rankedActions);
        return new InvestmentDailyFarmingPlan(
            allyCode,
            generatedAtUtc,
            farmingPlan.HasInventorySnapshot,
            farmingPlan.InventoryCapturedAtUtc,
            farmingPlan.ActiveTargetCount,
            farmingPlan.MissingResourceTypes,
            baselines,
            rankedActions,
            crystalBudget,
            eta.ResourceEtas,
            eta.TargetEtas,
            budgetScenarios,
            summary,
            "La energía actual, los fragmentos, el gear no inventariado, las tiendas en vivo y tus ingresos de cristales no son públicos. La ETA usa tasas empíricas conservadoras para Signal Data y para los farms de feedstock de Carbonite/Bronzium; no acredita el beneficio simultáneo de Sector 9 ni otras fuentes secundarias. Cualquier bloqueo sin cadencia fiable queda explícitamente fuera de la fecha completa.");
    }

    private static DailyFarmingAction BuildResourceAction(FarmingResourcePriority resource)
    {
        return resource.ResourceId switch
        {
            "carbonite_circuit_board" => NormalEnergy(
                resource,
                "Light Side 1-C (Normal)",
                6,
                "Farmea gear barato de este nodo como feedstock del Chatarrero para Carbonite Circuit Boards."),
            "bronzium_wiring" => NormalEnergy(
                resource,
                "Light Side 7-B (Normal)",
                10,
                "Prioriza este nodo como fuente de gear útil para convertir en Bronzium Wiring, sin sacrificar reservas necesarias para equipar personajes."),
            "signal_data_corrupted" => Scavenger(
                resource,
                "Chatarrero · conversión de Signal Data",
                "Convierte Signal Data sobrante en Corrupted Signal Data cuando R10 sea el bloqueo prioritario.",
                DailyFarmingPrecision.Exact),
            "credits" => Credits(resource),
            _ when resource.Lane == FarmingLane.AdvancedRelic => Stores(resource),
            _ => Scavenger(
                resource,
                "Chatarrero",
                $"Convierte solo gear sobrante en {resource.ResourceName}; conserva piezas necesarias para tus personajes activos.",
                DailyFarmingPrecision.Guided)
        };
    }

    private static DailyFarmingAction NormalEnergy(
        FarmingResourcePriority resource,
        string node,
        int energyCost,
        string action) => new(
            0,
            DailyFarmingChannel.NormalEnergy,
            "Energía normal",
            DailyFarmingPrecision.Guided,
            "Feedstock sugerido",
            $"Alimenta {resource.ResourceName}",
            action,
            resource.ResourceId,
            resource.ResourceName,
            node,
            energyCost,
            NormalEnergyPerDay,
            resource.Missing,
            resource.AffectedTargetCount,
            resource.SharedBottleneck,
            resource.Priority,
            StopCondition(resource));

    private static DailyFarmingAction Scavenger(
        FarmingResourcePriority resource,
        string source,
        string action,
        DailyFarmingPrecision precision) => new(
            0,
            DailyFarmingChannel.Scavenger,
            "Chatarrero",
            precision,
            precision == DailyFarmingPrecision.Exact ? "Fuente exacta" : "Conversión guiada",
            $"Convierte para {resource.ResourceName}",
            action,
            resource.ResourceId,
            resource.ResourceName,
            source,
            null,
            null,
            resource.Missing,
            resource.AffectedTargetCount,
            resource.SharedBottleneck,
            resource.Priority,
            StopCondition(resource));

    private static DailyFarmingAction Stores(FarmingResourcePriority resource) => new(
        0,
        DailyFarmingChannel.Stores,
        "Tiendas y eventos",
        DailyFarmingPrecision.Check,
        "Revisión",
        $"Busca {resource.ResourceName}",
        "Revisa las tiendas y recompensas disponibles hoy. No se marca una compra concreta porque disponibilidad y precios no están expuestos por el proveedor público.",
        resource.ResourceId,
        resource.ResourceName,
        "Tiendas / eventos / recompensas",
        null,
        null,
        resource.Missing,
        resource.AffectedTargetCount,
        resource.SharedBottleneck,
        resource.Priority,
        StopCondition(resource));

    private static DailyFarmingAction Credits(FarmingResourcePriority resource) => new(
        0,
        DailyFarmingChannel.Credits,
        "Créditos",
        DailyFarmingPrecision.Check,
        "Reserva",
        "Protege el colchón de créditos",
        "No desvíes energía únicamente por créditos: reserva el importe pendiente para que la siguiente subida de reliquia no se bloquee al final.",
        resource.ResourceId,
        resource.ResourceName,
        "Ingresos diarios / eventos / objetivos",
        null,
        null,
        resource.Missing,
        resource.AffectedTargetCount,
        resource.SharedBottleneck,
        resource.Priority,
        StopCondition(resource));

    private static DailyFarmingAction BuildFleetGuardrailAction(InvestmentFarmingPlan plan) => new(
        0,
        DailyFarmingChannel.FleetEnergy,
        "Energía de flota",
        DailyFarmingPrecision.Check,
        "Sin nodo directo",
        "Mantén Fleet en tus farms de gear o planos activos",
        "El modelo de reliquias actual no conoce un nodo Fleet exacto que deba desplazar tus farms de gear/planos. No gastes Fleet a ciegas solo por un déficit de relic mats.",
        null,
        null,
        "Fleet Battles",
        null,
        FleetEnergyPerDay,
        null,
        plan.ActiveTargetCount,
        false,
        "Media",
        "Reasigna Fleet cuando incorporemos gear/shards concretos al planificador.");

    private static string StopCondition(FarmingResourcePriority resource) =>
        resource.Missing is long missing
            ? $"Objetivo del snapshot: cubrir {missing:N0} unidades pendientes y volver a actualizar inventario."
            : "Actualiza el inventario para convertir este requisito en un déficit exacto.";

    private static IReadOnlyCollection<DailyEnergyBaseline> EnergyBaselines() =>
    [
        new(
            DailyFarmingChannel.CantinaEnergy,
            "Cantina",
            120,
            45,
            CantinaEnergyPerDay,
            "Base gratuita teórica: regeneración diaria más la energía bonus, evitando cap."),
        new(
            DailyFarmingChannel.NormalEnergy,
            "Normal",
            240,
            135,
            NormalEnergyPerDay,
            "Base gratuita teórica: regeneración diaria más tres bonus de 45, evitando cap."),
        new(
            DailyFarmingChannel.FleetEnergy,
            "Fleet",
            240,
            45,
            FleetEnergyPerDay,
            "Base gratuita teórica: regeneración diaria más el bonus de 45, evitando cap.")
    ];

    private static string BuildSummary(
        InvestmentFarmingPlan farmingPlan,
        IReadOnlyCollection<DailyFarmingAction> actions)
    {
        if (farmingPlan.ActiveTargetCount == 0)
        {
            return "No hay objetivos activos. Marca uno en Unit 360 para generar el plan diario.";
        }

        DailyFarmingAction? first = actions.FirstOrDefault(action => action.ResourceId is not null);
        if (first is null)
        {
            return "Hay objetivos activos, pero todavía no existe un déficit material accionable.";
        }

        return $"Empieza por {first.ChannelLabel}: {first.Title}. Después continúa por orden hasta que cambie tu inventario o tus objetivos.";
    }

    private static int PriorityOrder(string priority) => priority switch
    {
        "Crítica" => 0,
        "Alta" => 1,
        "Media" => 2,
        "Cubierto" => 3,
        _ => 2
    };

    private static int ChannelOrder(DailyFarmingChannel channel) => channel switch
    {
        DailyFarmingChannel.CantinaEnergy => 0,
        DailyFarmingChannel.NormalEnergy => 1,
        DailyFarmingChannel.Scavenger => 2,
        DailyFarmingChannel.Stores => 3,
        DailyFarmingChannel.Credits => 4,
        DailyFarmingChannel.FleetEnergy => 5,
        _ => 6
    };
}
