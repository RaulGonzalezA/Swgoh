using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Conquest;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Conquest;

public interface IConquestDailyPlanService
{
    Task<ConquestDailyPlanResult?> BuildAsync(
        long allyCode,
        ConquestDailyPlanRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class ConquestDailyPlanService(
    IConquestPlanRepository repository,
    IPlayerProfileService playerProfileService,
    ISwgohGameDataCatalog gameDataCatalog) : IConquestDailyPlanService
{
    private const int TeamSize = 5;
    private const int CandidatePoolSize = 30;
    private const int SeedCount = 18;
    private const decimal DiskFeatSynergyBonus = 1.5m;
    private const decimal MaximumDiskScoreBonus = 20m;

    public async Task<ConquestDailyPlanResult?> BuildAsync(
        long allyCode,
        ConquestDailyPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxBattles is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaxBattles,
                "Daily plan battles must be between 1 and 20.");
        }

        Task<ConquestPlan?> planTask = repository.GetCurrentAsync(allyCode, cancellationToken);
        Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(allyCode, cancellationToken);
        Task<GameDataCatalog> catalogTask = gameDataCatalog.GetAsync(cancellationToken);
        await Task.WhenAll(planTask, playerTask, catalogTask).ConfigureAwait(false);

        ConquestPlan? plan = await planTask.ConfigureAwait(false);
        if (plan is null)
        {
            return null;
        }

        ConquestFeat[] startingPending = [.. plan.Feats.Where(feat => !feat.IsComplete)];
        PlayerProfile? player = await playerTask.ConfigureAwait(false);
        GameDataCatalog catalog = await catalogTask.ConfigureAwait(false);
        if (player is null)
        {
            return EmptyResult(plan, request.MaxBattles, startingPending, "RosterUnavailable");
        }

        var progress = plan.Feats.ToDictionary(feat => feat.Id, feat => feat.Progress);
        var stamina = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var steps = new List<ConquestDailyPlanStep>();
        HashSet<string>? previousTeam = null;
        Guid? previousLoadoutId = null;
        string stopReason = "BattleLimitReached";

        for (int battle = 1; battle <= request.MaxBattles; battle++)
        {
            ConquestFeat[] pending =
            [
                .. plan.Feats
                    .Select(feat => Project(feat, progress[feat.Id]))
                    .Where(feat => !feat.IsComplete)
            ];
            if (pending.Length == 0)
            {
                stopReason = "AllFeatsCompleted";
                break;
            }

            CandidateUnit[] allCharacters =
            [
                .. player.Roster
                    .Where(unit => !unit.IsShip)
                    .Select(unit => ToCandidate(unit, catalog, pending, plan, CurrentStamina(unit.DefinitionId, plan, stamina)))
                    .Where(candidate => candidate is not null)
                    .Select(candidate => candidate!)
            ];
            CandidateUnit[] pool = BuildPool(allCharacters);
            TeamCandidate? best = BuildTeams(pool, pending, plan)
                .OrderByDescending(team => team.FeatEfficiency)
                .ThenByDescending(team => team.Score)
                .FirstOrDefault();
            if (best is null)
            {
                stopReason = allCharacters.Length == 0 ? "NoUsableCharacters" : "NoViableTeam";
                break;
            }

            ConquestDailyFeatProgress[] featProgress =
            [
                .. best.Contributions.Select(contribution =>
                {
                    ConquestFeat source = pending.Single(feat => feat.Id == contribution.FeatId);
                    int before = progress[source.Id];
                    int after = Math.Min(source.Target, before + contribution.ExpectedProgress);
                    return new ConquestDailyFeatProgress(
                        source.Id,
                        source.Name,
                        source.Points,
                        before,
                        after,
                        source.Target,
                        before < source.Target && after >= source.Target);
                })
            ];

            var teamIds = best.Units
                .Select(candidate => candidate.Unit.DefinitionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Guid? loadoutId = best.DiskLoadout?.LoadoutId;
            bool changesTeam = previousTeam is not null && !previousTeam.SetEquals(teamIds);
            bool changesLoadout = previousLoadoutId is not null && previousLoadoutId != loadoutId;
            int completedThisBattle = featProgress.Count(value => value.CompletedByBattle);
            string completionNote = completedThisBattle > 0
                ? $" Completa {completedThisBattle} hazaña(s)."
                : string.Empty;
            string rotationNote = changesTeam
                ? " Rota equipo respecto al combate anterior."
                : string.Empty;
            string diskNote = best.DiskLoadout is null
                ? string.Empty
                : $" Usa el preset {best.DiskLoadout.LoadoutName}.";

            steps.Add(new ConquestDailyPlanStep(
                battle,
                best.Score,
                best.FeatEfficiency,
                best.AverageStamina,
                best.PostBattleAverageStamina,
                best.ReserveRiskUnits,
                changesTeam,
                changesLoadout,
                best.DiskLoadout,
                [.. best.Units.Select(candidate => candidate.View)],
                featProgress,
                $"Avanza {best.Contributions.Count} hazaña(s).{completionNote}{rotationNote}{diskNote}"));

            foreach (CandidateUnit candidate in best.Units)
            {
                stamina[candidate.Unit.DefinitionId] = candidate.View.ExpectedPostBattleStamina;
            }

            foreach (ConquestDailyFeatProgress item in featProgress)
            {
                progress[item.FeatId] = item.AfterProgress;
            }

            previousTeam = teamIds;
            previousLoadoutId = loadoutId;
        }

        Guid[] remainingFeatIds =
        [
            .. plan.Feats
                .Where(feat => progress[feat.Id] < feat.Target)
                .Select(feat => feat.Id)
        ];
        int remainingStartingFeats = startingPending.Count(feat => progress[feat.Id] < feat.Target);
        int projectedCompleted = startingPending.Length - remainingStartingFeats;
        if (remainingStartingFeats == 0)
        {
            stopReason = "AllFeatsCompleted";
        }
        else if (steps.Count >= request.MaxBattles)
        {
            stopReason = "BattleLimitReached";
        }

        ConquestDailyRecoveryUnit[] recovery =
        [
            .. player.Roster
                .Where(unit => !unit.IsShip)
                .Select(unit => new
                {
                    Unit = unit,
                    FinalStamina = CurrentStamina(unit.DefinitionId, plan, stamina)
                })
                .Where(value => value.FinalStamina < plan.ReserveFloorPercent)
                .Select(value =>
                {
                    catalog.Units.TryGetValue(value.Unit.DefinitionId, out GameUnitDefinition? definition);
                    return new ConquestDailyRecoveryUnit(
                        value.Unit.DefinitionId,
                        definition?.Name ?? value.Unit.DefinitionId,
                        definition?.ThumbnailName,
                        value.FinalStamina,
                        plan.ReserveFloorPercent);
                })
                .OrderBy(value => value.FinalStamina)
                .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .Take(12)
        ];

        return new ConquestDailyPlanResult(
            plan.AllyCode,
            plan.EventId,
            request.MaxBattles,
            steps.Count,
            startingPending.Length,
            projectedCompleted,
            remainingStartingFeats,
            stopReason,
            steps,
            recovery,
            remainingFeatIds);
    }

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
            stopReason,
            [],
            [],
            [.. startingPending.Select(feat => feat.Id)]);

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
