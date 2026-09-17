namespace Swgoh.Application.Investments;

internal static class InvestmentRecommendationBuilder
{
    private const decimal CrossModuleBonus = 8m;
    private const decimal ConcreteTargetBonus = 5m;
    private const decimal MaximumScore = 100m;

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
                .OrderByDescending(item => item.Score)
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
            Priority(score),
            SuggestedAction(currentRelic, currentStars, targetRelic, targetStars),
            impacts);
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
}
