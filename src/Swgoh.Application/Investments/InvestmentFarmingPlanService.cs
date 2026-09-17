using Swgoh.Application.Abstractions;

namespace Swgoh.Application.Investments;

public interface IInvestmentFarmingPlanService
{
    Task<InvestmentFarmingPlan> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default);
}

internal sealed class InvestmentFarmingPlanService(
    IInvestmentTargetService targetService,
    IPlayerInventoryService inventoryService,
    IClock clock) : IInvestmentFarmingPlanService
{
    public async Task<InvestmentFarmingPlan> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyCollection<InvestmentTargetProgress>> targetsTask =
            targetService.GetAllAsync(allyCode, cancellationToken);
        Task<PlayerInventorySnapshot?> inventoryTask = inventoryService.GetAsync(allyCode, cancellationToken);
        await Task.WhenAll(targetsTask, inventoryTask).ConfigureAwait(false);

        InvestmentTargetProgress[] activeTargets =
        [
            .. (await targetsTask.ConfigureAwait(false))
                .Where(target => !target.Completed)
        ];
        PlayerInventorySnapshot? inventory = await inventoryTask.ConfigureAwait(false);
        IReadOnlyDictionary<string, long> available = BuildAvailableInventory(inventory);
        TargetRequirement[] targetRequirements =
        [
            .. activeTargets.Select(BuildTargetRequirement)
        ];

        FarmingResourcePriority[] resources = BuildResourcePriorities(targetRequirements, inventory, available);
        var sharedResourceIds = resources
            .Where(resource => resource.SharedBottleneck)
            .Select(resource => resource.ResourceId)
            .ToHashSet(StringComparer.Ordinal);
        FarmingTargetPlan[] targetPlans =
        [
            .. targetRequirements
                .Select(requirement => BuildTargetPlan(requirement, inventory, available, sharedResourceIds))
                .OrderByDescending(target => target.SharedBottlenecks)
                .ThenByDescending(target => target.BlockingResourceTypes)
                .ThenBy(target => target.MaterialCoverage ?? 1m)
                .ThenBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
        ];

        return new InvestmentFarmingPlan(
            allyCode,
            clock.UtcNow,
            inventory?.CapturedAtUtc,
            inventory?.Source,
            activeTargets.Length,
            targetRequirements.Count(requirement => requirement.Requirements.Count > 0),
            targetRequirements.Count(requirement => requirement.Requirements.Count == 0 && requirement.Target.StarStepsRemaining > 0),
            resources.Count(resource => resource.Missing is > 0),
            resources.Count(resource => resource.SharedBottleneck),
            resources,
            targetPlans);
    }

    private static TargetRequirement BuildTargetRequirement(InvestmentTargetProgress target)
    {
        IReadOnlyDictionary<string, long> requirements =
            target.TargetRelicTier is int targetRelic && targetRelic > target.CurrentRelicTier
                ? RelicMaterialRequirements.Calculate(target.CurrentRelicTier, targetRelic)
                : new Dictionary<string, long>(StringComparer.Ordinal);
        return new TargetRequirement(target, requirements);
    }

    private static FarmingResourcePriority[] BuildResourcePriorities(
        IReadOnlyCollection<TargetRequirement> targetRequirements,
        PlayerInventorySnapshot? inventory,
        IReadOnlyDictionary<string, long> available)
    {
        FarmingResourcePriority[] unordered =
        [
            .. targetRequirements
                .SelectMany(requirement => requirement.Requirements.Select(pair => new
                {
                    requirement.Target.DefinitionId,
                    requirement.Target.Name,
                    ResourceId = pair.Key,
                    Required = pair.Value
                }))
                .GroupBy(item => item.ResourceId, StringComparer.Ordinal)
                .Select(group =>
                {
                    InventoryResourceDefinition definition = PlayerInventoryCatalog.Resources
                        .First(resource => string.Equals(resource.Id, group.Key, StringComparison.Ordinal));
                    long required = group.Sum(item => item.Required);
                    long? owned = inventory is null ? null : available.GetValueOrDefault(group.Key);
                    long? missing = owned is null ? null : Math.Max(0, required - owned.Value);
                    decimal? coverage = owned is null || required == 0
                        ? null
                        : Math.Round(Math.Min(1m, (decimal)owned.Value / required), 3);
                    FarmingTargetDependency[] targets =
                    [
                        .. group
                            .GroupBy(item => new { item.DefinitionId, item.Name })
                            .Select(items => new FarmingTargetDependency(
                                items.Key.DefinitionId,
                                items.Key.Name,
                                items.Sum(item => item.Required)))
                            .OrderByDescending(item => item.Required)
                            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    ];
                    bool shared = targets.Length > 1 && (missing is null || missing > 0);
                    return new FarmingResourcePriority(
                        0,
                        definition.Id,
                        definition.Name,
                        definition.Kind,
                        Lane(definition),
                        LaneLabel(definition),
                        Action(definition),
                        required,
                        owned,
                        missing,
                        coverage,
                        targets.Length,
                        shared,
                        Priority(targets.Length, missing, coverage),
                        targets);
                })
                .OrderBy(resource => resource.Missing == 0)
                .ThenByDescending(resource => resource.SharedBottleneck)
                .ThenByDescending(resource => resource.AffectedTargetCount)
                .ThenBy(resource => resource.Coverage ?? 0m)
                .ThenByDescending(resource => resource.Missing ?? resource.Required)
                .ThenBy(resource => CatalogOrder(resource.ResourceId))
                .Select((resource, index) => resource with { Rank = index + 1 })
        ];
        return unordered;
    }

    private static FarmingTargetPlan BuildTargetPlan(
        TargetRequirement requirement,
        PlayerInventorySnapshot? inventory,
        IReadOnlyDictionary<string, long> available,
        IReadOnlySet<string> sharedResourceIds)
    {
        InvestmentTargetProgress target = requirement.Target;
        bool tracked = requirement.Requirements.Count > 0;
        decimal? coverage = null;
        int blocking = 0;
        if (tracked && inventory is not null)
        {
            decimal[] coverageByResource =
            [
                .. requirement.Requirements.Select(pair => pair.Value == 0
                    ? 1m
                    : Math.Min(1m, (decimal)available.GetValueOrDefault(pair.Key) / pair.Value))
            ];
            coverage = Math.Round(coverageByResource.Average(), 3);
            blocking = requirement.Requirements.Count(pair => available.GetValueOrDefault(pair.Key) < pair.Value);
        }

        int shared = requirement.Requirements.Keys.Count(sharedResourceIds.Contains);
        return new FarmingTargetPlan(
            target.DefinitionId,
            target.Name,
            target.ThumbnailName,
            target.CurrentRelicTier,
            target.TargetRelicTier,
            target.CurrentStars,
            target.TargetStars,
            target.RelicStepsRemaining,
            target.StarStepsRemaining,
            coverage,
            blocking,
            shared,
            tracked,
            TargetSummary(target, inventory, tracked, coverage, blocking, shared));
    }

    private static string TargetSummary(
        InvestmentTargetProgress target,
        PlayerInventorySnapshot? inventory,
        bool tracked,
        decimal? coverage,
        int blocking,
        int shared)
    {
        if (!tracked && target.StarStepsRemaining > 0)
        {
            return "El objetivo pendiente es de estrellas; los fragmentos no forman parte del inventario material disponible.";
        }

        if (inventory is null)
        {
            return "Añade un snapshot de inventario para convertir los requisitos de reliquia en déficit real.";
        }

        if (blocking == 0)
        {
            return "Los materiales de reliquia contabilizados para este objetivo están cubiertos individualmente.";
        }

        string competition = shared > 0
            ? $" {shared} de esos recursos compiten con otros objetivos."
            : string.Empty;
        return $"{blocking} tipos de material pendientes; cobertura {coverage:P0}.{competition}";
    }

    private static IReadOnlyDictionary<string, long> BuildAvailableInventory(PlayerInventorySnapshot? inventory) =>
        inventory?.Resources
            .GroupBy(resource => resource.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(resource => resource.Quantity), StringComparer.Ordinal)
        ?? new Dictionary<string, long>(StringComparer.Ordinal);

    private static FarmingLane Lane(InventoryResourceDefinition definition)
    {
        if (definition.Kind == InventoryResourceKind.Currency)
        {
            return FarmingLane.Credits;
        }

        if (definition.Kind == InventoryResourceKind.SignalData)
        {
            return FarmingLane.SignalData;
        }

        return definition.SortOrder >= 80 && definition.SortOrder <= 120
            ? FarmingLane.AdvancedRelic
            : FarmingLane.Scavenger;
    }

    private static string LaneLabel(InventoryResourceDefinition definition) => Lane(definition) switch
    {
        FarmingLane.Credits => "Créditos",
        FarmingLane.SignalData => "Cantina · Signal Data",
        FarmingLane.Scavenger => "Chatarrero",
        FarmingLane.AdvancedRelic => "Reliquias avanzadas",
        _ => "Recursos"
    };

    private static string Action(InventoryResourceDefinition definition) => Lane(definition) switch
    {
        FarmingLane.Credits => "Reserva créditos para las subidas de reliquia de tus objetivos activos.",
        FarmingLane.SignalData => "Prioriza la energía de Cantina destinada a este Signal Data.",
        FarmingLane.Scavenger => "Prioriza conversiones de gear en el Chatarrero para este material.",
        FarmingLane.AdvancedRelic => "Prioriza las fuentes disponibles de este material de reliquia avanzado.",
        _ => "Prioriza este recurso antes de ampliar nuevos objetivos."
    };

    private static string Priority(int affectedTargets, long? missing, decimal? coverage)
    {
        if (missing == 0)
        {
            return "Cubierto";
        }

        if (affectedTargets >= 2 && (coverage is null || coverage < 0.5m))
        {
            return "Crítica";
        }

        if (affectedTargets >= 2 || coverage is null || coverage < 0.5m)
        {
            return "Alta";
        }

        return "Media";
    }

    private static int CatalogOrder(string resourceId) => PlayerInventoryCatalog.Resources
        .First(resource => string.Equals(resource.Id, resourceId, StringComparison.Ordinal))
        .SortOrder;

    private sealed record TargetRequirement(
        InvestmentTargetProgress Target,
        IReadOnlyDictionary<string, long> Requirements);
}
