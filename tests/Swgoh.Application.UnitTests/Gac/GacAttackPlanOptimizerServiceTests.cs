using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacAttackPlanOptimizerServiceTests
{
    private const long PlayerAllyCode = 123456789;
    private const long OpponentAllyCode = 987654321;

    [Fact]
    public void Optimize_PrefersObservedCounterOverHigherPowerFallback()
    {
        Guid defenseId = Guid.NewGuid();
        GacTeamPresetDetails observed = Preset("Observed", ["A", "B", "C"], 20_000);
        GacTeamPresetDetails bruteForce = Preset("Brute force", ["D", "E", "F"], 40_000);
        GacVisibleDefenseDetails defense = Defense(defenseId, "Enemy", ["X", "Y", "Z"], 22_000);
        GacPlannerCounterHint hint = new(
            defenseId,
            "Enemy",
            "High",
            "ObservedCounterStatistics",
            "Observed exact counter.",
            false,
            observed.Id,
            observed.Squad.AllUnits,
            25,
            0.92m,
            0.88m,
            56.4m,
            18);
        GacPlannerState state = State([observed, bruteForce], [defense], [], [], [hint]);

        GacAttackOptimizationResult result = GacAttackPlanOptimizerService.Optimize(
            state,
            GacAttackOptimizationMode.RebuildPlanned);

        GacAttackOptimizationRecommendation recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(observed.Id, recommendation.TeamPresetId);
        Assert.Equal("Histórico observado", recommendation.Evidence);
        Assert.Equal(1, result.HistoricalMatches);
    }

    [Fact]
    public void Optimize_MaximizesCoverageWithoutReusingOverlappingUnits()
    {
        Guid firstDefenseId = Guid.NewGuid();
        Guid secondDefenseId = Guid.NewGuid();
        GacTeamPresetDetails first = Preset("First", ["A", "B", "C"], 25_000);
        GacTeamPresetDetails overlapping = Preset("Overlapping", ["A", "D", "E"], 25_000);
        GacTeamPresetDetails clean = Preset("Clean", ["F", "G", "H"], 24_000);
        GacVisibleDefenseDetails firstDefense = Defense(firstDefenseId, "Enemy one", ["X", "Y", "Z"], 22_000);
        GacVisibleDefenseDetails secondDefense = Defense(secondDefenseId, "Enemy two", ["Q", "R", "S"], 22_000);
        GacPlannerCounterHint firstHint = Hint(firstDefenseId, first, "Enemy one", 0.9m);
        GacPlannerCounterHint secondHint = Hint(secondDefenseId, overlapping, "Enemy two", 0.95m);
        GacPlannerState state = State(
            [first, overlapping, clean],
            [firstDefense, secondDefense],
            [],
            [],
            [firstHint, secondHint]);

        GacAttackOptimizationResult result = GacAttackPlanOptimizerService.Optimize(
            state,
            GacAttackOptimizationMode.RebuildPlanned);

        Assert.Equal(2, result.RecommendedAttacks);
        Guid[] selected = [.. result.Recommendations.Select(item => item.TeamPresetId)];
        Assert.Contains(clean.Id, selected);
        Assert.False(selected.Contains(first.Id) && selected.Contains(overlapping.Id));
    }

    [Fact]
    public void Optimize_DoesNotUseUnitsPlacedOnOwnDefense()
    {
        Guid defenseId = Guid.NewGuid();
        GacTeamPresetDetails ownDefense = Preset("My defense", ["A", "B", "C"], 25_000, GacPlannerTeamUse.Defense);
        GacTeamPresetDetails overlapping = Preset("Overlap", ["A", "D", "E"], 28_000);
        GacTeamPresetDetails clean = Preset("Clean", ["F", "G", "H"], 24_000);
        GacVisibleDefenseDetails enemy = Defense(defenseId, "Enemy", ["X", "Y", "Z"], 22_000);
        var ownAssignment = new GacOwnDefenseAssignmentDetails(Guid.NewGuid(), "Sur frontal", ownDefense);
        GacPlannerState state = State(
            [ownDefense, overlapping, clean],
            [enemy],
            [ownAssignment],
            [],
            []);

        GacAttackOptimizationResult result = GacAttackPlanOptimizerService.Optimize(
            state,
            GacAttackOptimizationMode.RebuildPlanned);

        GacAttackOptimizationRecommendation recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(clean.Id, recommendation.TeamPresetId);
    }

    [Fact]
    public void Optimize_FillGapsPreservesExistingPlannedDefenseWhileRebuildIncludesIt()
    {
        Guid firstDefenseId = Guid.NewGuid();
        Guid secondDefenseId = Guid.NewGuid();
        GacTeamPresetDetails plannedTeam = Preset("Planned", ["A", "B", "C"], 24_000);
        GacTeamPresetDetails freeTeam = Preset("Free", ["D", "E", "F"], 24_000);
        GacVisibleDefenseDetails firstDefense = Defense(firstDefenseId, "Enemy one", ["X", "Y", "Z"], 22_000);
        GacVisibleDefenseDetails secondDefense = Defense(secondDefenseId, "Enemy two", ["Q", "R", "S"], 22_000);
        var plannedAttack = new GacAttackAssignmentDetails(
            Guid.NewGuid(),
            firstDefenseId,
            plannedTeam,
            1,
            GacAttackPlanStatus.Planned,
            null);
        GacPlannerState state = State(
            [plannedTeam, freeTeam],
            [firstDefense, secondDefense],
            [],
            [plannedAttack],
            []);

        GacAttackOptimizationResult fill = GacAttackPlanOptimizerService.Optimize(
            state,
            GacAttackOptimizationMode.FillGaps);
        GacAttackOptimizationResult rebuild = GacAttackPlanOptimizerService.Optimize(
            state,
            GacAttackOptimizationMode.RebuildPlanned);

        Assert.Single(fill.Recommendations);
        Assert.Equal(secondDefenseId, fill.Recommendations.Single().DefenseId);
        Assert.Equal(2, rebuild.TargetDefenses);
        Assert.Equal(2, rebuild.RecommendedAttacks);
    }

    [Fact]
    public async Task OptimizeCurrentAsync_WhenApplied_PersistsRecommendedAttack()
    {
        Guid defenseId = Guid.NewGuid();
        GacTeamPresetDetails team = Preset("Attack", ["A", "B", "C"], 25_000);
        GacVisibleDefenseDetails defense = Defense(defenseId, "Enemy", ["X", "Y", "Z"], 22_000);
        GacPlannerState state = State([team], [defense], [], [], []);
        GacRoundPlan plan = GacRoundPlan.Create(
            PlayerAllyCode,
            OpponentAllyCode,
            "event",
            "event-instance",
            1,
            GacFormat.ThreeVsThree,
            GacLeague.Kyber,
            DateTimeOffset.UtcNow);
        plan.Replace(
            [],
            [GacVisibleDefense.Create(
                defenseId,
                "Sur frontal",
                "Enemy",
                GacPlannerSquad.Create(GacFormat.ThreeVsThree, "X", ["Y", "Z"], false))],
            [],
            DateTimeOffset.UtcNow);
        var repository = new FakePlanRepository(plan);
        var service = new GacAttackPlanOptimizerService(
            new FakePlannerService(state),
            repository,
            new FixedClock(new DateTimeOffset(2026, 9, 13, 19, 0, 0, TimeSpan.Zero)));

        GacAttackOptimizationLookup lookup = await service.OptimizeCurrentAsync(
            PlayerAllyCode,
            GacAttackOptimizationMode.RebuildPlanned,
            apply: true,
            TestContext.Current.CancellationToken);

        Assert.True(lookup.Optimization?.Applied);
        GacAttackAssignment attack = Assert.Single(repository.Plan.Attacks);
        Assert.Equal(defenseId, attack.DefenseId);
        Assert.Equal(team.Id, attack.TeamPresetId);
        Assert.Equal(GacAttackPlanStatus.Planned, attack.Status);
    }

    private static GacPlannerState State(
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        IReadOnlyCollection<GacVisibleDefenseDetails> defenses,
        IReadOnlyCollection<GacOwnDefenseAssignmentDetails> ownDefenses,
        IReadOnlyCollection<GacAttackAssignmentDetails> attacks,
        IReadOnlyCollection<GacPlannerCounterHint> hints)
    {
        var opponent = new CurrentGacOpponent(
            PlayerAllyCode,
            OpponentAllyCode,
            "Opponent",
            "opponent-id",
            GacLeague.Kyber,
            GacFormat.ThreeVsThree,
            "event",
            "event-instance",
            "bracket",
            1,
            "test",
            "test");
        var plan = new GacRoundPlanDetails(
            GacRoundPlan.BuildId(PlayerAllyCode, "event-instance", 1),
            PlayerAllyCode,
            OpponentAllyCode,
            "Opponent",
            "event",
            "event-instance",
            1,
            GacFormat.ThreeVsThree,
            GacLeague.Kyber,
            ownDefenses,
            defenses,
            attacks,
            [],
            hints,
            DateTimeOffset.UtcNow);
        return new GacPlannerState(opponent, presets, plan);
    }

    private static GacTeamPresetDetails Preset(
        string name,
        IReadOnlyCollection<string> ids,
        long unitPower,
        GacPlannerTeamUse use = GacPlannerTeamUse.Offense)
    {
        string leader = ids.First();
        GacPlannerUnitDetails[] units = [.. ids.Select(id => Unit(id, unitPower))];
        return new GacTeamPresetDetails(
            Guid.NewGuid(),
            PlayerAllyCode,
            name,
            GacFormat.ThreeVsThree,
            use,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            DateTimeOffset.UtcNow);
    }

    private static GacVisibleDefenseDetails Defense(
        Guid id,
        string name,
        IReadOnlyCollection<string> ids,
        long unitPower)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(unitId => Unit(unitId, unitPower))];
        units[0] = units[0] with { Name = name };
        return new GacVisibleDefenseDetails(
            id,
            "Sur frontal",
            null,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            false);
    }

    private static GacPlannerCounterHint Hint(
        Guid defenseId,
        GacTeamPresetDetails preset,
        string threatName,
        decimal winRate) => new(
            defenseId,
            threatName,
            "High",
            "ObservedCounterStatistics",
            "Observed counter.",
            false,
            preset.Id,
            preset.Squad.AllUnits,
            20,
            winRate,
            0.85m,
            55m,
            12);

    private static GacPlannerUnitDetails Unit(string id, long power) => new(
        id,
        id,
        null,
        false,
        power,
        7,
        1,
        0);

    private sealed class FakePlannerService(GacPlannerState state) : IGacPlannerService
    {
        public Task<GacPlannerLookup> GetCurrentAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GacPlannerLookup(CurrentGacOpponentStatus.Found, null, state));

        public Task<GacPlannerLookup> SaveCurrentAsync(
            long allyCode,
            SaveCurrentGacRoundPlan input,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GacTeamPresetDetails> CreatePresetAsync(
            long allyCode,
            SaveGacTeamPreset input,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GacTeamPresetDetails?> UpdatePresetAsync(
            long allyCode,
            Guid id,
            SaveGacTeamPreset input,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeletePresetAsync(
            long allyCode,
            Guid id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakePlanRepository(GacRoundPlan plan) : IGacRoundPlanRepository
    {
        public GacRoundPlan Plan { get; private set; } = plan;

        public Task<GacRoundPlan?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<GacRoundPlan?>(Plan.Id == id ? Plan : null);

        public Task UpsertAsync(GacRoundPlan updated, CancellationToken cancellationToken = default)
        {
            Plan = updated;
            return Task.CompletedTask;
        }
    }

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
