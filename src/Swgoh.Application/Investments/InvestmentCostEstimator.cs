namespace Swgoh.Application.Investments;

internal static class InvestmentCostEstimator
{
    private static readonly decimal[] RelicTransitionCosts =
    [
        0m, 24m, 6m, 7m, 8m, 10m, 13m, 18m, 30m, 44m
    ];

    private static readonly decimal[] StarTransitionCosts =
    [
        0m, 18m, 4m, 6m, 8m, 12m, 18m, 28m
    ];

    public static InvestmentCostEstimate? Estimate(
        int currentRelicTier,
        int currentStars,
        int? targetRelicTier,
        int? targetStars)
    {
        int relicSteps = Math.Max(0, (targetRelicTier ?? currentRelicTier) - currentRelicTier);
        int starSteps = Math.Max(0, (targetStars ?? currentStars) - currentStars);
        if (relicSteps == 0 && starSteps == 0)
        {
            return null;
        }

        decimal relicCost = EstimateRelicCost(currentRelicTier, targetRelicTier);
        decimal starCost = EstimateStarCost(currentStars, targetStars);
        decimal total = Math.Round(relicCost + starCost, 1);

        return new InvestmentCostEstimate(
            total,
            CostBand(total),
            relicSteps,
            starSteps,
            IsEstimate: true,
            Summary(relicSteps, starSteps, total));
    }

    private static decimal EstimateRelicCost(int current, int? target)
    {
        if (target is not int targetTier || targetTier <= current)
        {
            return 0m;
        }

        decimal cost = 0m;
        for (int tier = current + 1; tier <= targetTier; tier++)
        {
            cost += tier < RelicTransitionCosts.Length
                ? RelicTransitionCosts[tier]
                : RelicTransitionCosts[^1] + ((tier - (RelicTransitionCosts.Length - 1)) * 18m);
        }

        return cost;
    }

    private static decimal EstimateStarCost(int current, int? target)
    {
        if (target is not int targetStars || targetStars <= current)
        {
            return 0m;
        }

        decimal cost = 0m;
        for (int star = current + 1; star <= targetStars; star++)
        {
            int index = Math.Clamp(star, 1, StarTransitionCosts.Length - 1);
            cost += StarTransitionCosts[index];
        }

        return cost;
    }

    private static string CostBand(decimal cost) => cost switch
    {
        <= 12m => "Muy bajo",
        <= 25m => "Bajo",
        <= 45m => "Medio",
        <= 75m => "Alto",
        _ => "Muy alto"
    };

    private static string Summary(int relicSteps, int starSteps, decimal cost)
    {
        var parts = new List<string>(2);
        if (relicSteps > 0)
        {
            parts.Add($"{relicSteps} salto{(relicSteps == 1 ? string.Empty : "s")} de reliquia");
        }

        if (starSteps > 0)
        {
            parts.Add($"{starSteps} salto{(starSteps == 1 ? string.Empty : "s")} de estrellas");
        }

        return $"{string.Join(" + ", parts)} · índice de coste {cost:0.#}. Es una estimación relativa: no descuenta tu inventario ni progreso parcial de fragmentos.";
    }
}
