namespace Swgoh.Application.Investments;

internal static class DailyEnergyRefreshPlanner
{
    private const int RefreshEnergy = 120;
    private const int MaximumDailyBudget = 5_000;

    private static readonly IReadOnlyDictionary<DailyFarmingChannel, int[]> RefreshCosts =
        new Dictionary<DailyFarmingChannel, int[]>
        {
            [DailyFarmingChannel.NormalEnergy] =
                [50, 50, 50, 100, 100, 100, 200, 200, 200, 400, 400, 400, 800, 800, 800],
            [DailyFarmingChannel.FleetEnergy] =
                [50, 50, 50, 100, 100, 100, 200, 200, 200, 400, 400, 400, 800, 800, 800],
            [DailyFarmingChannel.CantinaEnergy] =
                [100, 100, 100, 200, 200, 400, 400, 800, 800, 800, 1_600, 1_600, 3_200, 3_200]
        };

    public static DailyCrystalBudgetPlan Build(
        int dailyCrystalBudget,
        IReadOnlyCollection<DailyFarmingAction> actions,
        IReadOnlyCollection<DailyEnergyBaseline> baselines)
    {
        int budget = Math.Clamp(dailyCrystalBudget, 0, MaximumDailyBudget);
        var actionableByChannel = actions
            .Where(IsRefreshEligible)
            .GroupBy(action => action.Channel)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(action => PriorityOrder(action.Priority))
                    .ThenBy(action => action.Precision)
                    .ThenByDescending(action => action.SharedBottleneck)
                    .ThenByDescending(action => action.AffectedTargetCount)
                    .First());

        var refreshCounts = actionableByChannel.Keys
            .ToDictionary(channel => channel, _ => 0);
        int spent = 0;

        while (true)
        {
            RefreshCandidate? next = actionableByChannel
                .Select(pair => NextCandidate(
                    pair.Key,
                    pair.Value,
                    refreshCounts[pair.Key],
                    budget - spent))
                .Where(candidate => candidate is not null)
                .Cast<RefreshCandidate>()
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Cost)
                .ThenBy(candidate => ChannelOrder(candidate.Channel))
                .FirstOrDefault();

            if (next is null)
            {
                break;
            }

            refreshCounts[next.Channel]++;
            spent += next.Cost;
        }

        DailyRefreshRecommendation[] refreshes =
        [
            .. refreshCounts
                .Where(pair => pair.Value > 0)
                .Select(pair => BuildRecommendation(
                    pair.Key,
                    pair.Value,
                    actionableByChannel[pair.Key],
                    baselines))
                .OrderBy(refresh => ChannelOrder(refresh.Channel))
        ];

        return new DailyCrystalBudgetPlan(
            budget,
            spent,
            budget - spent,
            ProfileLabel(budget),
            refreshes);
    }

    private static bool IsRefreshEligible(DailyFarmingAction action) =>
        action.ResourceId is not null
        && action.Channel is DailyFarmingChannel.NormalEnergy
            or DailyFarmingChannel.CantinaEnergy
            or DailyFarmingChannel.FleetEnergy
        && RefreshCosts.ContainsKey(action.Channel);

    private static RefreshCandidate? NextCandidate(
        DailyFarmingChannel channel,
        DailyFarmingAction action,
        int refreshCount,
        int remainingBudget)
    {
        int[] costs = RefreshCosts[channel];
        if (refreshCount >= costs.Length)
        {
            return null;
        }

        int cost = costs[refreshCount];
        if (cost > remainingBudget)
        {
            return null;
        }

        decimal baseScore = PriorityScore(action.Priority)
            + PrecisionScore(action.Precision)
            + (action.SharedBottleneck ? 60m : 0m)
            + Math.Min(50m, action.AffectedTargetCount * 10m);

        decimal diminishingScore = baseScore / (refreshCount + 1m);
        return new RefreshCandidate(channel, cost, diminishingScore);
    }

    private static DailyRefreshRecommendation BuildRecommendation(
        DailyFarmingChannel channel,
        int refreshCount,
        DailyFarmingAction action,
        IReadOnlyCollection<DailyEnergyBaseline> baselines)
    {
        int[] costs = RefreshCosts[channel];
        int crystalCost = costs.Take(refreshCount).Sum();
        int energyGained = refreshCount * RefreshEnergy;
        int baseline = baselines
            .FirstOrDefault(item => item.Channel == channel)
            ?.BaselineFreeEnergy ?? 0;
        int? nextRefreshCost = refreshCount < costs.Length ? costs[refreshCount] : null;
        return new DailyRefreshRecommendation(
            channel,
            action.ChannelLabel,
            refreshCount,
            crystalCost,
            energyGained,
            baseline,
            baseline + energyGained,
            nextRefreshCost,
            $"Refuerza {action.Title.ToLowerInvariant()} porque es la prioridad {action.Priority.ToLowerInvariant()} del canal.");
    }

    private static string ProfileLabel(int budget) => budget switch
    {
        0 => "F2P",
        50 => "Ahorro · 50",
        150 => "Eficiente · 150",
        300 => "Acelerado · 300",
        _ => $"Personalizado · {budget}"
    };

    private static decimal PriorityScore(string priority) => priority switch
    {
        "Crítica" => 400m,
        "Alta" => 300m,
        "Media" => 200m,
        _ => 100m
    };

    private static decimal PrecisionScore(DailyFarmingPrecision precision) => precision switch
    {
        DailyFarmingPrecision.Exact => 40m,
        DailyFarmingPrecision.Guided => 20m,
        _ => 0m
    };

    private static int PriorityOrder(string priority) => priority switch
    {
        "Crítica" => 0,
        "Alta" => 1,
        "Media" => 2,
        _ => 3
    };

    private static int ChannelOrder(DailyFarmingChannel channel) => channel switch
    {
        DailyFarmingChannel.CantinaEnergy => 0,
        DailyFarmingChannel.NormalEnergy => 1,
        DailyFarmingChannel.FleetEnergy => 2,
        _ => 3
    };

    private sealed record RefreshCandidate(
        DailyFarmingChannel Channel,
        int Cost,
        decimal Score);
}
