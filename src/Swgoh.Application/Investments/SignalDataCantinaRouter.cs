namespace Swgoh.Application.Investments;

internal static class SignalDataCantinaRouter
{
    private static readonly PairRoute[] PairRoutes =
    [
        new("signal_data_fragmented", "signal_data_incomplete", "Cantina 9-B", 1),
        new("signal_data_fragmented", "signal_data_flawed", "Cantina 9-D", 2),
        new("signal_data_incomplete", "signal_data_flawed", "Cantina 9-F", 3)
    ];

    public static IReadOnlyCollection<DailyFarmingAction> Build(
        IEnumerable<FarmingResourcePriority> resources,
        int baselineFreeEnergy)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentOutOfRangeException.ThrowIfNegative(baselineFreeEnergy);

        Dictionary<string, FarmingResourcePriority> pending = resources
            .Where(IsRoutableSignalData)
            .GroupBy(resource => resource.ResourceId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        if (pending.Count == 0)
        {
            return [];
        }

        var actions = new List<DailyFarmingAction>(pending.Count);
        PairCandidate? pair = FindBestPair(pending);
        if (pair is not null)
        {
            actions.Add(BuildPairAction(pair, baselineFreeEnergy));
            pending.Remove(pair.Left.ResourceId);
            pending.Remove(pair.Right.ResourceId);
        }

        actions.AddRange(pending.Values
            .OrderBy(resource => PriorityOrder(resource.Priority))
            .ThenByDescending(resource => resource.SharedBottleneck)
            .ThenByDescending(resource => resource.AffectedTargetCount)
            .ThenByDescending(resource => resource.Missing ?? 0)
            .Select(resource => BuildSingleAction(resource, baselineFreeEnergy)));

        return actions;
    }

    private static PairCandidate? FindBestPair(
        IReadOnlyDictionary<string, FarmingResourcePriority> pending) =>
        PairRoutes
            .Where(route => pending.ContainsKey(route.LeftResourceId)
                && pending.ContainsKey(route.RightResourceId))
            .Select(route =>
            {
                FarmingResourcePriority left = pending[route.LeftResourceId];
                FarmingResourcePriority right = pending[route.RightResourceId];
                return new PairCandidate(
                    route,
                    left,
                    right,
                    ResourceScore(left) + ResourceScore(right),
                    (left.Missing ?? 0) + (right.Missing ?? 0));
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.TotalMissing)
            .ThenBy(candidate => candidate.Route.SortOrder)
            .FirstOrDefault();

    private static DailyFarmingAction BuildPairAction(
        PairCandidate candidate,
        int baselineFreeEnergy)
    {
        FarmingResourcePriority left = candidate.Left;
        FarmingResourcePriority right = candidate.Right;
        int affectedTargets = left.Targets
            .Concat(right.Targets)
            .Select(target => target.DefinitionId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        bool shared = left.SharedBottleneck
            || right.SharedBottleneck
            || affectedTargets > 1;
        string priority = PriorityOrder(left.Priority) <= PriorityOrder(right.Priority)
            ? left.Priority
            : right.Priority;
        string deficits = string.Join(
            " · ",
            new[] { DescribeDeficit(left), DescribeDeficit(right) });

        return new DailyFarmingAction(
            0,
            DailyFarmingChannel.CantinaEnergy,
            "Energía de Cantina",
            DailyFarmingPrecision.Exact,
            "Nodo dual exacto",
            $"Farmea {left.ResourceName} + {right.ResourceName}",
            $"Usa {candidate.Route.Node} mientras ambos déficits estén activos. El nodo puede entregar los dos tipos en el mismo intento. {deficits}.",
            $"cantina_pair:{left.ResourceId}+{right.ResourceId}",
            $"{left.ResourceName} + {right.ResourceName}",
            candidate.Route.Node,
            20,
            baselineFreeEnergy,
            null,
            affectedTargets,
            shared,
            priority,
            $"Mantén {candidate.Route.Node} mientras falten ambos tipos; cuando uno quede cubierto, cambia al nodo 8 dedicado del recurso restante.");
    }

    private static DailyFarmingAction BuildSingleAction(
        FarmingResourcePriority resource,
        int baselineFreeEnergy)
    {
        string node = resource.ResourceId switch
        {
            "signal_data_fragmented" => "Cantina 8-C",
            "signal_data_incomplete" => "Cantina 8-F",
            "signal_data_flawed" => "Cantina 8-G",
            _ => throw new ArgumentOutOfRangeException(
                nameof(resource),
                resource.ResourceId,
                "Unsupported Signal Data resource.")
        };

        return new DailyFarmingAction(
            0,
            DailyFarmingChannel.CantinaEnergy,
            "Energía de Cantina",
            DailyFarmingPrecision.Exact,
            "Nodo exacto",
            $"Farmea {resource.ResourceName}",
            $"Gasta primero la energía de Cantina destinada a reliquias en {node}.",
            resource.ResourceId,
            resource.ResourceName,
            node,
            16,
            baselineFreeEnergy,
            resource.Missing,
            resource.AffectedTargetCount,
            resource.SharedBottleneck,
            resource.Priority,
            StopCondition(resource));
    }

    private static bool IsRoutableSignalData(FarmingResourcePriority resource) =>
        resource.ResourceId is "signal_data_fragmented"
            or "signal_data_incomplete"
            or "signal_data_flawed";

    private static long ResourceScore(FarmingResourcePriority resource)
    {
        long priority = PriorityOrder(resource.Priority) switch
        {
            0 => 4_000_000L,
            1 => 3_000_000L,
            2 => 2_000_000L,
            _ => 1_000_000L
        };
        long shared = resource.SharedBottleneck ? 250_000L : 0L;
        long affected = Math.Min(20, resource.AffectedTargetCount) * 10_000L;
        long missing = Math.Min(9_999L, resource.Missing ?? 0L);
        return priority + shared + affected + missing;
    }

    private static string DescribeDeficit(FarmingResourcePriority resource) =>
        resource.Missing is long missing
            ? $"{resource.ResourceName}: faltan {missing:N0}"
            : $"{resource.ResourceName}: déficit pendiente de snapshot";

    private static string StopCondition(FarmingResourcePriority resource) =>
        resource.Missing is long missing
            ? $"Objetivo del snapshot: cubrir {missing:N0} unidades pendientes y volver a actualizar inventario."
            : "Actualiza el inventario para convertir este requisito en un déficit exacto.";

    private static int PriorityOrder(string priority) => priority switch
    {
        "Crítica" => 0,
        "Alta" => 1,
        "Media" => 2,
        "Cubierto" => 3,
        _ => 2
    };

    private sealed record PairRoute(
        string LeftResourceId,
        string RightResourceId,
        string Node,
        int SortOrder);

    private sealed record PairCandidate(
        PairRoute Route,
        FarmingResourcePriority Left,
        FarmingResourcePriority Right,
        long Score,
        long TotalMissing);
}
