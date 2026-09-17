namespace Swgoh.Application.Investments;

internal static class InvestmentRecommendationBuilder
{
    private const decimal CrossModuleBonus = 8m;
    private const decimal ConcreteTargetBonus = 5m;
    private const decimal MaximumScore = 100m;
    private const decimal ImpactWeight = 0.65m;
    private const decimal EfficiencyWeight = 0.35m;

    public static IReadOnlyCollection<InvestmentRecommendation> Build(
        IEnumerable<InvestmentSignal> signals,
        int maximumRecommendations = 20)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecommendations, 1);

        return
        [
            .. signals
                .Where(signal => !string.IsNullOrWhiteSpace(signal.DefinitionId))
                .GroupBy(signal => signal.DefinitionId, StringComparer.OrdinalIgnoreCase)
                .Select(BuildRecommendation)
                .OrderByDescending(item => item.ValueScore)
                .ThenByDescending(item => item.Score)
                .ThenByDescending(item => item.ModuleCount)
                .ThenByDescending(item => item.HasConcreteTarget)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(maximumRecommendations)
                .Select((item, index) => item with { Rank = index + 1 })
        ];
    }

    private static InvestmentRecommendation BuildRecommendation(IGrouping<string, InvestmentSignal> group)
    {
        InvestmentSignal[] values = [.. group];
        InvestmentSignal identity = values
            .OrderByDescending(value => !string.IsNullOrWhiteSpace(value.ThumbnailName))
            .ThenByDescending(value => value.CurrentRelicTier)
            .ThenByDescending(value => value.CurrentStars)
            .First();

        InvestmentModuleImpact[] impacts =
        [
            .. values
                .GroupBy(value => value.Module)
                .Select(BuildModuleImpact)
                .OrderByDescending(impact => impact.Score)
                .ThenBy(impact => impact.Module)
        ];

        int currentRelic = values.Max(value => value.CurrentRelicTier);
        int currentStars = values.Max(value => value.CurrentStars);
        int? targetRelic = values
            .Where(value => value.TargetRelicTier is > 0)
            .Select(value => value.TargetRelicTier)
            .Max();
        int? targetStars = values
            .Where(value => value.TargetStars is > 0)
            .Select(value => value.TargetStars)
            .DefaultIfEmpty()
            .Min();
        if (targetStars == 0)
        {
            targetStars = null;
        }

        decimal moduleScore = impacts.Sum(impact => impact.Score);
        decimal crossModule = Math.Max(0, impacts.Length - 1) * CrossModuleBonus;
        decimal concrete = impacts.Any(impact => impact.ConcreteTarget) ? ConcreteTargetBonus : 0m;
        decimal score = Math.Min(MaximumScore, Math.Round(moduleScore + crossModule + concrete, 1));
        InvestmentCostEstimate? estimatedCost = InvestmentCostEstimator.Estimate(
            currentRelic,
            currentStars,
            targetRelic,
            targetStars);
        decimal? impactPerCost = estimatedCost is null || estimatedCost.CostIndex <= 0m
            ? null
            : Math.Round(score / estimatedCost.CostIndex, 2);
        decimal valueScore = CalculateValueScore(score, impactPerCost, estimatedCost is not null);

        return new InvestmentRecommendation(
            0,
            identity.DefinitionId,
            identity.Name,
            identity.ThumbnailName,
            currentRelic,
            currentStars,
            targetRelic,
            targetStars,
            score,
            valueScore,
            impactPerCost,
            Priority(score),
            ValueRating(valueScore, estimatedCost is not null),
            SuggestedAction(currentRelic, currentStars, targetRelic, targetStars),
            BenefitSummary(impacts, targetRelic, targetStars),
            estimatedCost,
            impacts);
    }

    private static decimal CalculateValueScore(decimal score, decimal? impactPerCost, bool hasCost)
    {
        if (!hasCost || impactPerCost is null)
        {
            return Math.Round(score * 0.85m, 1);
        }

        decimal efficiencyScore = Math.Min(MaximumScore, impactPerCost.Value * 35m);
        return Math.Round((score * ImpactWeight) + (efficiencyScore * EfficiencyWeight), 1);
    }

    private static InvestmentModuleImpact BuildModuleImpact(IGrouping<InvestmentModule, InvestmentSignal> group)
    {
        InvestmentSignal[] values = [.. group];
        decimal score = Math.Min(45m, values.Sum(value => value.Score));
        int? targetRelic = values
            .Where(value => value.TargetRelicTier is > 0)
            .Select(value => value.TargetRelicTier)
            .Max();
        int? targetStars = values
            .Where(value => value.TargetStars is > 0)
            .Select(value => value.TargetStars)
            .DefaultIfEmpty()
            .Min();
        if (targetStars == 0)
        {
            targetStars = null;
        }

        string[] reasons =
        [
            .. values
                .Select(value => value.Reason)
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        return new InvestmentModuleImpact(
            group.Key,
            Math.Round(score, 1),
            string.Join(" ", reasons),
            targetRelic,
            targetStars,
            values.Any(value => value.ConcreteTarget));
    }

    private static string Priority(decimal score) => score switch
    {
        >= 70m => "Crítica",
        >= 50m => "Alta",
        >= 30m => "Media",
        _ => "Oportunidad"
    };

    private static string ValueRating(decimal valueScore, bool hasCost) => (hasCost, valueScore) switch
    {
        (false, _) => "Estratégica",
        (true, >= 70m) => "Excelente",
        (true, >= 55m) => "Muy buena",
        (true, >= 40m) => "Buena",
        _ => "Selectiva"
    };

    private static string SuggestedAction(
        int currentRelic,
        int currentStars,
        int? targetRelic,
        int? targetStars)
    {
        var actions = new List<string>(2);
        if (targetRelic is int relic && relic > currentRelic)
        {
            actions.Add($"R{currentRelic} → R{relic}");
        }

        if (targetStars is int stars && stars > currentStars)
        {
            actions.Add($"{currentStars}★ → {stars}★");
        }

        return actions.Count > 0
            ? string.Join(" · ", actions)
            : "Prioridad estratégica: refuerza mods, habilidades y supervivencia según el módulo.";
    }

    private static string BenefitSummary(
        IReadOnlyCollection<InvestmentModuleImpact> impacts,
        int? targetRelic,
        int? targetStars)
    {
        string moduleText = impacts.Count == 1
            ? "1 módulo"
            : $"{impacts.Count} módulos";
        string targetText = targetRelic is not null || targetStars is not null
            ? " con un objetivo verificable"
            : " como inversión estratégica";
        return $"Concentra impacto en {moduleText}{targetText}.";
    }
}
