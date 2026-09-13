using Swgoh.Application.Abstractions;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Conquest;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Conquest;

public interface IConquestService
{
    Task<ConquestPlanDetails?> GetCurrentAsync(long allyCode, CancellationToken cancellationToken = default);

    Task<ConquestPlanDetails> SaveAsync(
        long allyCode,
        SaveConquestPlan input,
        CancellationToken cancellationToken = default);

    Task<ConquestOptimizationResult?> OptimizeCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default);
}

internal sealed class ConquestService(
    IConquestPlanRepository repository,
    IPlayerProfileService playerProfileService,
    ISwgohGameDataCatalog gameDataCatalog,
    IClock clock) : IConquestService
{
    private const int TeamSize = 5;
    private const int CandidatePoolSize = 30;
    private const int SeedCount = 18;
    private const int RecommendationCount = 8;

    public async Task<ConquestPlanDetails?> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        ConquestPlan? plan = await repository.GetCurrentAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return plan is null ? null : ToDetails(plan);
    }

    public async Task<ConquestPlanDetails> SaveAsync(
        long allyCode,
        SaveConquestPlan input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.EventId);
        ConquestFeat[] feats = [.. input.Feats.Select(ToDomain)];
        string id = ConquestPlan.BuildId(allyCode, input.EventId);
        ConquestPlan? existing = await repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);

        ConquestPlan plan;
        if (existing is null)
        {
            plan = ConquestPlan.Create(
                allyCode,
                input.EventId,
                input.Name,
                input.Difficulty,
                feats,
                clock.UtcNow);
        }
        else
        {
            existing.Replace(input.Name, input.Difficulty, feats, clock.UtcNow);
            plan = existing;
        }

        await repository.UpsertAsync(plan, cancellationToken).ConfigureAwait(false);
        return ToDetails(plan);
    }

    public async Task<ConquestOptimizationResult?> OptimizeCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        Task<ConquestPlan?> planTask = repository.GetCurrentAsync(allyCode, cancellationToken);
        Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(allyCode, cancellationToken);
        Task<GameDataCatalog> catalogTask = gameDataCatalog.GetAsync(cancellationToken);
        await Task.WhenAll(planTask, playerTask, catalogTask).ConfigureAwait(false);

        ConquestPlan? plan = await planTask.ConfigureAwait(false);
        if (plan is null)
        {
            return null;
        }

        PlayerProfile? player = await playerTask.ConfigureAwait(false);
        if (player is null)
        {
            return new ConquestOptimizationResult(allyCode, plan.EventId, 0, 0, [], []);
        }

        ConquestFeat[] pending = [.. plan.Feats.Where(feat => !feat.IsComplete)];
        if (pending.Length == 0)
        {
            return new ConquestOptimizationResult(allyCode, plan.EventId, 0, 0, [], []);
        }

        GameDataCatalog catalog = await catalogTask.ConfigureAwait(false);
        CandidateUnit[] allCharacters =
        [
            .. player.Roster
                .Where(unit => !unit.IsShip)
                .Select(unit => ToCandidate(unit, catalog, pending))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
        ];
        if (allCharacters.Length == 0)
        {
            return new ConquestOptimizationResult(
                allyCode,
                plan.EventId,
                pending.Length,
                0,
                [],
                [.. pending.Select(feat => feat.Id)]);
        }

        CandidateUnit[] relevant =
        [
            .. allCharacters
                .OrderByDescending(candidate => candidate.FeatWeight)
                .ThenByDescending(candidate => candidate.Unit.GalacticPower)
                .Take(CandidatePoolSize)
        ];
        CandidateUnit[] tacticalFillers =
        [
            .. allCharacters
                .OrderByDescending(candidate => candidate.Unit.GalacticPower)
                .Take(10)
        ];
        CandidateUnit[] pool =
        [
            .. relevant
                .Concat(tacticalFillers)
                .GroupBy(candidate => candidate.Unit.DefinitionId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(CandidatePoolSize)
        ];

        List<TeamCandidate> teams = BuildTeams(pool, pending);
        ConquestTeamRecommendation[] recommendations =
        [
            .. teams
                .OrderByDescending(team => team.FeatEfficiency)
                .ThenByDescending(team => team.Score)
                .Take(RecommendationCount)
                .Select((team, index) => ToRecommendation(team, index + 1))
        ];
        HashSet<Guid> covered = recommendations
            .SelectMany(recommendation => recommendation.AdvancesFeats)
            .Select(contribution => contribution.FeatId)
            .ToHashSet();

        return new ConquestOptimizationResult(
            allyCode,
            plan.EventId,
            pending.Length,
            pool.Length,
            recommendations,
            [.. pending.Where(feat => !covered.Contains(feat.Id)).Select(feat => feat.Id)]);
    }

    private static List<TeamCandidate> BuildTeams(
        IReadOnlyCollection<CandidateUnit> pool,
        IReadOnlyCollection<ConquestFeat> pending)
    {
        var teams = new Dictionary<string, TeamCandidate>(StringComparer.Ordinal);
        CandidateUnit[] seeds =
        [
            .. pool
                .OrderByDescending(candidate => candidate.FeatWeight)
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
                        Evaluation = EvaluateTeam([.. selected, candidate], pending)
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
                AddTeam(teams, EvaluateTeam(selected, pending));
            }
        }

        CandidateUnit[] strongest =
        [
            .. pool.OrderByDescending(candidate => candidate.Unit.GalacticPower).Take(TeamSize)
        ];
        if (strongest.Length == TeamSize)
        {
            AddTeam(teams, EvaluateTeam(strongest, pending));
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
        IReadOnlyCollection<ConquestFeat> pending)
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
        decimal? averageSpeed = AverageNullable(team.Select(candidate => candidate.Unit.Stats?.Speed));
        decimal relicDepth = team.Average(candidate => (decimal)candidate.Unit.RelicTier);
        decimal tactical = Math.Min(14m, totalGp / 30_000m) + Math.Min(6m, relicDepth * 0.75m);
        decimal score = Math.Round(Math.Clamp(
            (featEfficiency * 4m) + (contributions.Length * 5m) + tactical,
            0m,
            100m), 1);

        return new TeamCandidate(team, contributions, score, featEfficiency, totalGp, averageSpeed);
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
        IReadOnlyCollection<ConquestFeat> pending)
    {
        if (!catalog.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? definition) || definition.IsShip)
        {
            return null;
        }

        var view = new ConquestOptimizationUnit(
            unit.DefinitionId,
            definition.Name,
            definition.ThumbnailName,
            unit.RelicTier,
            unit.GalacticPower,
            unit.Stats?.Speed,
            definition.Factions);
        decimal weight = pending
            .Where(feat => Matches(unit.DefinitionId, definition.Factions, feat.Rule))
            .Sum(feat => feat.Points / (decimal)Math.Max(1, feat.Remaining));
        return new CandidateUnit(unit, view, weight);
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

    private static decimal? AverageNullable(IEnumerable<decimal?> values)
    {
        decimal[] known = [.. values.Where(value => value is not null).Select(value => value!.Value)];
        return known.Length == 0 ? null : Math.Round(known.Average(), 1);
    }

    private static ConquestTeamRecommendation ToRecommendation(TeamCandidate team, int rank)
    {
        string featNames = string.Join(", ", team.Contributions.Select(item => item.FeatName));
        return new ConquestTeamRecommendation(
            rank,
            team.Score,
            team.FeatEfficiency,
            team.TeamGalacticPower,
            team.AverageSpeed,
            [.. team.Units.Select(candidate => candidate.View)],
            team.Contributions,
            $"Avanza {team.Contributions.Count} hazaña(s) en la misma batalla: {featNames}.");
    }

    private static ConquestFeat ToDomain(SaveConquestFeat input) => ConquestFeat.Create(
        input.Id ?? Guid.NewGuid(),
        input.Name,
        input.Scope,
        input.Sector,
        input.Points,
        input.Target,
        input.Progress,
        input.ExpectedProgressPerBattle,
        ConquestFeatRule.Create(
            input.RuleType,
            input.Faction,
            input.UnitDefinitionIds,
            input.MinimumMatchingUnits));

    private static ConquestPlanDetails ToDetails(ConquestPlan plan)
    {
        ConquestFeatDetails[] feats =
        [
            .. plan.Feats.Select(feat => new ConquestFeatDetails(
                feat.Id,
                feat.Name,
                feat.Scope,
                feat.Sector,
                feat.Points,
                feat.Target,
                feat.Progress,
                feat.Remaining,
                feat.ExpectedProgressPerBattle,
                feat.Rule,
                feat.IsComplete))
        ];
        return new ConquestPlanDetails(
            plan.Id,
            plan.AllyCode,
            plan.EventId,
            plan.Name,
            plan.Difficulty,
            feats,
            feats.Count(feat => feat.IsComplete),
            feats.Length,
            feats.Where(feat => feat.IsComplete).Sum(feat => feat.Points),
            feats.Where(feat => !feat.IsComplete).Sum(feat => feat.Points),
            plan.UpdatedAtUtc);
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
        decimal? AverageSpeed);
}
