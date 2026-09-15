using Swgoh.Application.GameData;
using Swgoh.Domain.Conquest;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Conquest;

internal sealed partial class ConquestDailyPlanService
{
    private static ConquestDailyPlanResult EmptyResult(
        ConquestPlan plan,
        int requestedBattles,
        IReadOnlyCollection<ConquestFeat> startingPending,
        string stopReason) => new(
            plan.AllyCode,
            plan.EventId,
            requestedBattles,
            0,
            startingPending.Count,
            0,
            startingPending.Count,
            plan.AvailableEnergy,
            plan.EnergyCostPerBattle,
            0,
            plan.AvailableEnergy,
            plan.CurrentRewardPoints,
            plan.CurrentRewardPoints,
            0,
            plan.TargetRewardPoints,
            plan.RewardTargetName,
            plan.TargetRewardPoints is int target && plan.CurrentRewardPoints >= target,
            0m,
            stopReason,
            [],
            [],
            [.. startingPending.Select(feat => feat.Id)]);

    private static int RewardPointsUnlocked(TeamCandidate team, int rewardPointsNeeded)
    {
        int unlocked = team.Contributions
            .Where(contribution => contribution.ExpectedProgress >= contribution.Remaining)
            .Sum(contribution => contribution.Points);
        return rewardPointsNeeded > 0 ? Math.Min(unlocked, rewardPointsNeeded) : unlocked;
    }

    private static ConquestFeat Project(ConquestFeat feat, int progress) => ConquestFeat.Create(
        feat.Id,
        feat.Name,
        feat.Scope,
        feat.Sector,
        feat.Points,
        feat.Target,
        Math.Clamp(progress, 0, feat.Target),
        feat.ExpectedProgressPerBattle,
        feat.Rule);

    private static int CurrentStamina(
        string definitionId,
        ConquestPlan plan,
        IReadOnlyDictionary<string, int> simulated) =>
        simulated.TryGetValue(definitionId, out int value)
            ? value
            : plan.GetCurrentStamina(definitionId);

    private static CandidateUnit[] BuildPool(IReadOnlyCollection<CandidateUnit> allCharacters)
    {
        CandidateUnit[] relevant =
        [
            .. allCharacters
                .OrderByDescending(candidate => candidate.FeatWeight)
                .ThenByDescending(candidate => candidate.View.CurrentStamina)
                .ThenByDescending(candidate => candidate.Unit.GalacticPower)
                .Take(CandidatePoolSize)
        ];
        CandidateUnit[] tacticalFillers =
        [
            .. allCharacters
                .OrderByDescending(candidate => candidate.View.CurrentStamina)
                .ThenByDescending(candidate => candidate.Unit.GalacticPower)
                .Take(10)
        ];
        return
        [
            .. relevant
                .Concat(tacticalFillers)
                .GroupBy(candidate => candidate.Unit.DefinitionId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(CandidatePoolSize)
        ];
    }

    private static List<TeamCandidate> BuildTeams(
        IReadOnlyCollection<CandidateUnit> pool,
        IReadOnlyCollection<ConquestFeat> pending,
        ConquestPlan plan)
    {
        var teams = new Dictionary<string, TeamCandidate>(StringComparer.Ordinal);
        CandidateUnit[] seeds =
        [
            .. pool
                .OrderByDescending(candidate => candidate.FeatWeight)
                .ThenByDescending(candidate => candidate.View.CurrentStamina)
                .ThenByDescending(candidate => candidate.Unit.GalacticPower)
                .Take(SeedCount)
        ];

        foreach (CandidateUnit seed in seeds)
        {
            var selected = new List<CandidateUnit> { seed };
            while (selected.Count < TeamSize)
            {
                CandidateUnit? next = pool
                    .Where(candidate => selected.All(current => !string.Equals(
                        current.Unit.DefinitionId,
                        candidate.Unit.DefinitionId,
                        StringComparison.OrdinalIgnoreCase)))
                    .Select(candidate => new
                    {
                        Candidate = candidate,
                        Evaluation = EvaluateTeam([.. selected, candidate], pending, plan)
                    })
                    .OrderByDescending(value => value.Evaluation.FeatEfficiency)
                    .ThenByDescending(value => value.Evaluation.Score)
                    .Select(value => value.Candidate)
                    .FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                selected.Add(next);
            }

            if (selected.Count == TeamSize)
            {
                AddTeam(teams, EvaluateTeam(selected, pending, plan));
            }
        }

        CandidateUnit[] strongestRested =
        [
            .. pool
                .OrderByDescending(candidate => candidate.View.CurrentStamina)
                .ThenByDescending(candidate => candidate.Unit.GalacticPower)
                .Take(TeamSize)
        ];
        if (strongestRested.Length == TeamSize)
        {
            AddTeam(teams, EvaluateTeam(strongestRested, pending, plan));
        }

        return [.. teams.Values.Where(team => team.Contributions.Count > 0)];
    }

    private static void AddTeam(IDictionary<string, TeamCandidate> teams, TeamCandidate team)
    {
        string key = string.Join('|', team.Units
            .Select(unit => unit.Unit.DefinitionId)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        if (!teams.TryGetValue(key, out TeamCandidate? existing) || team.Score > existing.Score)
        {
            teams[key] = team;
        }
    }

    private static TeamCandidate EvaluateTeam(
        IReadOnlyCollection<CandidateUnit> team,
        IReadOnlyCollection<ConquestFeat> pending,
        ConquestPlan plan)
    {
        ConquestFeatContribution[] contributions =
        [
            .. pending
                .Select(feat => Contribution(team, feat))
                .Where(contribution => contribution is not null)
                .Select(contribution => contribution!)
        ];
        decimal featEfficiency = Math.Round(contributions.Sum(item => item.PointValueThisBattle), 2);
        long totalGp = team.Sum(candidate => candidate.Unit.GalacticPower);
        decimal relicDepth = team.Average(candidate => (decimal)candidate.Unit.RelicTier);
        decimal averageStamina = Math.Round(team.Average(candidate => (decimal)candidate.View.CurrentStamina), 1);
        decimal postBattleAverageStamina = Math.Round(
            team.Average(candidate => (decimal)candidate.View.ExpectedPostBattleStamina),
            1);
        int reserveRiskUnits = team.Count(candidate => candidate.View.BelowReserveAfterBattle);
        decimal staminaOpportunityCost = Math.Round(
            team.Sum(candidate => StaminaOpportunityCost(candidate.View, plan.ReserveFloorPercent)),
            1);
        ConquestDiskRecommendation? diskLoadout = BestDiskLoadout(team, contributions, plan);
        decimal diskScoreBonus = Math.Min(MaximumDiskScoreBonus, diskLoadout?.PlannerBonus ?? 0m);
        decimal readiness = 0.55m + (0.45m * averageStamina / 100m);
        decimal tacticalBase = Math.Min(14m, totalGp / 30_000m) + Math.Min(6m, relicDepth * 0.75m);
        decimal tactical = tacticalBase * readiness;
        decimal score = Math.Round(Math.Clamp(
            (featEfficiency * 4m) +
            (contributions.Length * 5m) +
            tactical +
            diskScoreBonus -
            staminaOpportunityCost,
            0m,
            100m), 1);

        return new TeamCandidate(
            team,
            contributions,
            score,
            featEfficiency,
            totalGp,
            averageStamina,
            postBattleAverageStamina,
            staminaOpportunityCost,
            reserveRiskUnits,
            diskLoadout);
    }

    private static decimal StaminaOpportunityCost(ConquestOptimizationUnit unit, int reserveFloorPercent)
    {
        decimal depletionPenalty = (100m - unit.CurrentStamina) / 25m;
        decimal reservePenalty = unit.ExpectedPostBattleStamina < reserveFloorPercent
            ? 2m + ((reserveFloorPercent - unit.ExpectedPostBattleStamina) / 10m * 1.5m)
            : 0m;
        decimal criticalPenalty = unit.ExpectedPostBattleStamina == 0 ? 4m : 0m;
        return depletionPenalty + reservePenalty + criticalPenalty;
    }

    private static ConquestFeatContribution? Contribution(
        IReadOnlyCollection<CandidateUnit> team,
        ConquestFeat feat)
    {
        int matching = team.Count(candidate => Matches(candidate, feat.Rule));
        if (matching < feat.Rule.MinimumMatchingUnits)
        {
            return null;
        }

        int expected = Math.Min(feat.Remaining, feat.ExpectedProgressPerBattle);
        decimal pointValue = feat.Remaining == 0
            ? 0m
            : Math.Round(feat.Points * (expected / (decimal)feat.Remaining), 2);
        return new ConquestFeatContribution(
            feat.Id,
            feat.Name,
            feat.Points,
            feat.Remaining,
            expected,
            pointValue);
    }

    private static CandidateUnit? ToCandidate(
        RosterUnit unit,
        GameDataCatalog catalog,
        IReadOnlyCollection<ConquestFeat> pending,
        ConquestPlan plan,
        int currentStamina)
    {
        if (!catalog.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition) || definition.IsShip)
        {
            return null;
        }

        if (currentStamina == 0)
        {
            return null;
        }

        int expectedPostBattleStamina = Math.Max(0, currentStamina - plan.StaminaCostPerBattle);
        var view = new ConquestOptimizationUnit(
            unit.DefinitionId,
            definition.Name,
            definition.ThumbnailName,
            unit.RelicTier,
            unit.GalacticPower,
            unit.Stats?.Speed,
            definition.Factions,
            currentStamina,
            expectedPostBattleStamina,
            expectedPostBattleStamina < plan.ReserveFloorPercent);
        decimal baseWeight = pending
            .Where(feat => Matches(unit.DefinitionId, definition.Factions, feat.Rule))
            .Sum(feat => feat.Points / (decimal)Math.Max(1, feat.Remaining));
        decimal readinessWeight = 0.65m + (0.35m * currentStamina / 100m);
        return new CandidateUnit(unit, view, baseWeight * readinessWeight);
    }

    private static bool Matches(CandidateUnit candidate, ConquestFeatRule rule) =>
        Matches(candidate.Unit.DefinitionId, candidate.View.Factions, rule);

    private static bool Matches(
        string definitionId,
        IReadOnlyCollection<string> factions,
        ConquestFeatRule rule) => rule.Type switch
        {
            ConquestFeatRuleType.AnyCharacter => true,
            ConquestFeatRuleType.Faction => factions.Any(faction => string.Equals(
                faction,
                rule.Faction,
                StringComparison.OrdinalIgnoreCase)),
            ConquestFeatRuleType.SpecificUnits => rule.UnitDefinitionIds.Any(value => string.Equals(
                value,
                definitionId,
                StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

    private static ConquestDiskRecommendation? BestDiskLoadout(
        IReadOnlyCollection<CandidateUnit> team,
        IReadOnlyCollection<ConquestFeatContribution> contributions,
        ConquestPlan plan)
    {
        if (plan.DiskLoadouts.Count == 0 || plan.DataDisks.Count == 0)
        {
            return null;
        }

        Dictionary<Guid, ConquestDataDisk> disksById = plan.DataDisks.ToDictionary(disk => disk.Id);
        HashSet<Guid> contributionFeatIds = contributions.Select(value => value.FeatId).ToHashSet();
        return plan.DiskLoadouts
            .Select(loadout =>
            {
                ConquestDataDisk[] loadoutDisks =
                [
                    .. loadout.DiskIds
                        .Where(disksById.ContainsKey)
                        .Select(id => disksById[id])
                ];
                ConquestDataDisk[] applicable = [.. loadoutDisks.Where(disk => DiskAppliesToTeam(disk, team))];
                Guid[] matchedFeatIds =
                [
                    .. applicable
                        .SelectMany(disk => disk.SupportedFeatIds)
                        .Where(contributionFeatIds.Contains)
                        .Distinct()
                ];
                decimal bonus = Math.Round(
                    applicable.Sum(disk => disk.PlannerBonus) +
                    (matchedFeatIds.Length * DiskFeatSynergyBonus),
                    1);
                return new ConquestDiskRecommendation(
                    loadout.Id,
                    loadout.Name,
                    loadoutDisks.Sum(disk => disk.CapacityCost),
                    plan.DiskCapacityLimit,
                    bonus,
                    loadoutDisks,
                    matchedFeatIds);
            })
            .OrderByDescending(value => value.PlannerBonus)
            .ThenBy(value => value.CapacityUsed)
            .FirstOrDefault();
    }

    private static bool DiskAppliesToTeam(ConquestDataDisk disk, IReadOnlyCollection<CandidateUnit> team)
    {
        int matching = disk.Target.Type switch
        {
            ConquestDataDiskTargetType.AnyTeam => team.Count,
            ConquestDataDiskTargetType.Faction => team.Count(candidate => candidate.View.Factions.Any(faction =>
                string.Equals(faction, disk.Target.Faction, StringComparison.OrdinalIgnoreCase))),
            ConquestDataDiskTargetType.SpecificUnits => team.Count(candidate => disk.Target.UnitDefinitionIds.Any(id =>
                string.Equals(id, candidate.Unit.DefinitionId, StringComparison.OrdinalIgnoreCase))),
            _ => 0
        };
        return matching >= disk.Target.MinimumMatchingUnits;
    }

    private sealed record CandidateUnit(
        RosterUnit Unit,
        ConquestOptimizationUnit View,
        decimal FeatWeight);

    private sealed record TeamCandidate(
        IReadOnlyCollection<CandidateUnit> Units,
        IReadOnlyCollection<ConquestFeatContribution> Contributions,
        decimal Score,
        decimal FeatEfficiency,
        long TeamGalacticPower,
        decimal AverageStamina,
        decimal PostBattleAverageStamina,
        decimal StaminaOpportunityCost,
        int ReserveRiskUnits,
        ConquestDiskRecommendation? DiskLoadout);
}
