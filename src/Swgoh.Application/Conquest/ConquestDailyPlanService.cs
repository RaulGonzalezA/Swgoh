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

internal sealed partial class ConquestDailyPlanService(
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
        if (plan.TargetRewardPoints is int initialTarget && plan.CurrentRewardPoints >= initialTarget)
        {
            return EmptyResult(plan, request.MaxBattles, startingPending, "RewardTargetReached");
        }

        if (plan.AvailableEnergy is int initialEnergy && initialEnergy < plan.EnergyCostPerBattle)
        {
            return EmptyResult(plan, request.MaxBattles, startingPending, "EnergyBudgetExhausted");
        }

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
        int energySpent = 0;
        int projectedRewardPoints = plan.CurrentRewardPoints;
        string stopReason = "BattleLimitReached";

        for (int battle = 1; battle <= request.MaxBattles; battle++)
        {
            if (plan.TargetRewardPoints is int target && projectedRewardPoints >= target)
            {
                stopReason = "RewardTargetReached";
                break;
            }

            if (plan.AvailableEnergy is int availableEnergy &&
                energySpent + plan.EnergyCostPerBattle > availableEnergy)
            {
                stopReason = "EnergyBudgetExhausted";
                break;
            }

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
                    .Select(unit => ToCandidate(
                        unit,
                        catalog,
                        pending,
                        plan,
                        CurrentStamina(unit.DefinitionId, plan, stamina)))
                    .Where(candidate => candidate is not null)
                    .Select(candidate => candidate!)
            ];
            CandidateUnit[] pool = BuildPool(allCharacters);
            IEnumerable<TeamCandidate> orderedTeams = BuildTeams(pool, pending, plan);
            if (plan.TargetRewardPoints is int rewardTarget)
            {
                int rewardPointsNeeded = Math.Max(0, rewardTarget - projectedRewardPoints);
                orderedTeams = orderedTeams
                    .OrderByDescending(team => RewardPointsUnlocked(team, rewardPointsNeeded))
                    .ThenByDescending(team => team.FeatEfficiency)
                    .ThenByDescending(team => team.Score);
            }
            else
            {
                orderedTeams = orderedTeams
                    .OrderByDescending(team => team.FeatEfficiency)
                    .ThenByDescending(team => team.Score);
            }

            TeamCandidate? best = orderedTeams.FirstOrDefault();
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
                    bool completed = before < source.Target && after >= source.Target;
                    return new ConquestDailyFeatProgress(
                        source.Id,
                        source.Name,
                        source.Points,
                        before,
                        after,
                        source.Target,
                        completed,
                        completed ? source.Points : 0);
                })
            ];

            int rewardPointsEarned = featProgress.Sum(value => value.RewardPointsGranted);
            energySpent += plan.EnergyCostPerBattle;
            projectedRewardPoints += rewardPointsEarned;
            decimal stepEfficiency = energySpent == 0
                ? 0m
                : Math.Round((projectedRewardPoints - plan.CurrentRewardPoints) / (decimal)energySpent, 3);
            bool rewardTargetReached = plan.TargetRewardPoints is int configuredTarget &&
                projectedRewardPoints >= configuredTarget;

            var teamIds = best.Units
                .Select(candidate => candidate.Unit.DefinitionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Guid? loadoutId = best.DiskLoadout?.LoadoutId;
            bool changesTeam = previousTeam is not null && !previousTeam.SetEquals(teamIds);
            bool changesLoadout = previousLoadoutId is not null && previousLoadoutId != loadoutId;
            int completedThisBattle = featProgress.Count(value => value.CompletedByBattle);
            string completionNote = completedThisBattle > 0
                ? $" Completa {completedThisBattle} hazaña(s) y suma {rewardPointsEarned} punto(s) de recompensa."
                : string.Empty;
            string targetNote = rewardTargetReached
                ? " Alcanza el objetivo de recompensa configurado."
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
                plan.EnergyCostPerBattle,
                energySpent,
                rewardPointsEarned,
                projectedRewardPoints,
                stepEfficiency,
                rewardTargetReached,
                best.AverageStamina,
                best.PostBattleAverageStamina,
                best.ReserveRiskUnits,
                changesTeam,
                changesLoadout,
                best.DiskLoadout,
                [.. best.Units.Select(candidate => candidate.View)],
                featProgress,
                $"Avanza {best.Contributions.Count} hazaña(s).{completionNote}{targetNote}{rotationNote}{diskNote}"));

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

            if (rewardTargetReached)
            {
                stopReason = "RewardTargetReached";
                break;
            }
        }

        Guid[] remainingFeatIds =
        [
            .. plan.Feats
                .Where(feat => progress[feat.Id] < feat.Target)
                .Select(feat => feat.Id)
        ];
        int remainingStartingFeats = startingPending.Count(feat => progress[feat.Id] < feat.Target);
        int projectedCompleted = startingPending.Length - remainingStartingFeats;
        bool targetReached = plan.TargetRewardPoints is int finalTarget && projectedRewardPoints >= finalTarget;
        bool energyExhausted = plan.AvailableEnergy is int available &&
            available - energySpent < plan.EnergyCostPerBattle;

        if (targetReached)
        {
            stopReason = "RewardTargetReached";
        }
        else if (remainingStartingFeats == 0)
        {
            stopReason = "AllFeatsCompleted";
        }
        else if (energyExhausted && steps.Count < request.MaxBattles)
        {
            stopReason = "EnergyBudgetExhausted";
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

        int rewardPointsGained = projectedRewardPoints - plan.CurrentRewardPoints;
        int? energyRemaining = plan.AvailableEnergy is int configuredEnergy
            ? Math.Max(0, configuredEnergy - energySpent)
            : null;
        decimal rewardPointsPerEnergy = energySpent == 0
            ? 0m
            : Math.Round(rewardPointsGained / (decimal)energySpent, 3);

        return new ConquestDailyPlanResult(
            plan.AllyCode,
            plan.EventId,
            request.MaxBattles,
            steps.Count,
            startingPending.Length,
            projectedCompleted,
            remainingStartingFeats,
            plan.AvailableEnergy,
            plan.EnergyCostPerBattle,
            energySpent,
            energyRemaining,
            plan.CurrentRewardPoints,
            projectedRewardPoints,
            rewardPointsGained,
            plan.TargetRewardPoints,
            plan.RewardTargetName,
            targetReached,
            rewardPointsPerEnergy,
            stopReason,
            steps,
            recovery,
            remainingFeatIds);
    }
}
