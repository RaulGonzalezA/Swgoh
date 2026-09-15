using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacAttackExecutionPostCommitTests
{
    private const long PlayerAllyCode = 123_456_789;
    private const long OpponentAllyCode = 987_654_321;

    [Fact]
    public async Task ExecuteAsync_WhenPersonalLearningFails_KeepsCommittedResultAndContinuesReoptimization()
    {
        Scenario scenario = BuildScenario();
        var optimizer = new StubOptimizer(
            new GacAttackOptimizationLookup(
                CurrentGacOpponentStatus.Found,
                null,
                scenario.State,
                EmptyOptimization()));
        var service = new GacAttackExecutionService(
            new StubPlannerService(scenario.State),
            scenario.Repository,
            new ThrowingPersonalBattleRepository(),
            optimizer,
            new FixedClock(scenario.Now.AddMinutes(1)));

        GacAttackExecutionLookup result = await service.ExecuteAsync(
            PlayerAllyCode,
            scenario.AttackId,
            new ExecuteGacAttackResult(GacAttackPlanStatus.Won, 60, "One-shot"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.Equal(GacAttackPlanStatus.Won, Assert.Single(scenario.Repository.Plan.Attacks).Status);
        Assert.Equal(1, optimizer.Calls);
        Assert.Contains(
            result.Execution!.PostCommitWarnings,
            warning => warning.Contains("aprendizaje personal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenReoptimizationFails_ReturnsCommittedStateWithoutThrowing()
    {
        Scenario scenario = BuildScenario();
        var personalRepository = new RecordingPersonalBattleRepository();
        var service = new GacAttackExecutionService(
            new StubPlannerService(scenario.State),
            scenario.Repository,
            personalRepository,
            new ThrowingOptimizer(),
            new FixedClock(scenario.Now.AddMinutes(1)));

        GacAttackExecutionLookup result = await service.ExecuteAsync(
            PlayerAllyCode,
            scenario.AttackId,
            new ExecuteGacAttackResult(GacAttackPlanStatus.Failed, 12, "Timeout"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.NotNull(result.Execution);
        Assert.Equal(GacAttackPlanStatus.Failed, result.Execution!.Status);
        Assert.Null(result.Execution.Optimization);
        Assert.Null(result.Execution.NextRecommendation);
        Assert.Equal(1, result.Execution.State.Plan.Version);
        GacAttackAssignmentDetails attack = Assert.Single(result.Execution.State.Plan.Attacks);
        Assert.Equal(GacAttackPlanStatus.Failed, attack.Status);
        Assert.Equal(12, attack.Banners);
        Assert.Single(personalRepository.Items);
        Assert.Contains(
            result.Execution.PostCommitWarnings,
            warning => warning.Contains("recalcular", StringComparison.OrdinalIgnoreCase));
    }

    private static Scenario BuildScenario()
    {
        DateTimeOffset now = new(2026, 9, 15, 14, 0, 0, TimeSpan.Zero);
        Guid presetId = Guid.NewGuid();
        Guid defenseId = Guid.NewGuid();
        Guid attackId = Guid.NewGuid();

        GacPlannerSquad attackSquad = GacPlannerSquad.Create(
            GacFormat.ThreeVsThree,
            "A1",
            ["A2", "A3"],
            isFleet: false);
        GacPlannerSquad defenseSquad = GacPlannerSquad.Create(
            GacFormat.ThreeVsThree,
            "D1",
            ["D2", "D3"],
            isFleet: false);
        GacRoundPlan plan = GacRoundPlan.Create(
            PlayerAllyCode,
            OpponentAllyCode,
            "event",
            "instance",
            1,
            GacFormat.ThreeVsThree,
            GacLeague.Kyber,
            now);
        plan.Replace(
            [],
            [GacVisibleDefense.Create(defenseId, "Sur frontal", "Defense", defenseSquad)],
            [GacAttackAssignment.Create(
                attackId,
                defenseId,
                presetId,
                1,
                GacAttackPlanStatus.Planned,
                "Planned")],
            now);

        GacTeamPresetDetails preset = new(
            presetId,
            PlayerAllyCode,
            "Attack",
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Offense,
            SquadDetails("A1", "A2", "A3"),
            now);
        GacPlannerState state = new(
            new CurrentGacOpponent(
                PlayerAllyCode,
                OpponentAllyCode,
                "Opponent",
                null,
                GacLeague.Kyber,
                GacFormat.ThreeVsThree,
                "event",
                "instance",
                "bracket",
                1,
                "test",
                "test"),
            [preset],
            new GacRoundPlanDetails(
                plan.Id,
                PlayerAllyCode,
                OpponentAllyCode,
                "Opponent",
                "event",
                "instance",
                1,
                GacFormat.ThreeVsThree,
                GacLeague.Kyber,
                [],
                [new GacVisibleDefenseDetails(
                    defenseId,
                    "Sur frontal",
                    "Defense",
                    SquadDetails("D1", "D2", "D3"),
                    Defeated: false)],
                [new GacAttackAssignmentDetails(
                    attackId,
                    defenseId,
                    preset,
                    1,
                    GacAttackPlanStatus.Planned,
                    "Planned")],
                [],
                [],
                now));

        return new Scenario(now, attackId, state, new RecordingRoundPlanRepository(plan));
    }

    private static GacPlannerSquadDetails SquadDetails(string leader, params string[] members) => new(
        Unit(leader),
        [.. members.Select(Unit)],
        IsFleet: false);

    private static GacPlannerUnitDetails Unit(string id) => new(
        id,
        id,
        null,
        IsShip: false,
        GalacticPower: 50_000,
        RelicTier: 7,
        ZetaCount: 1,
        OmicronCount: 0);

    private static GacAttackOptimizationResult EmptyOptimization() => new(
        GacAttackOptimizationMode.FillGaps,
        Applied: false,
        TargetDefenses: 0,
        RecommendedAttacks: 0,
        HistoricalMatches: 0,
        AverageScore: 0m,
        KnownAverageBanners: null,
        UncoveredDefenseIds: [],
        Recommendations: [],
        SearchLimitReached: false);

    private sealed record Scenario(
        DateTimeOffset Now,
        Guid AttackId,
        GacPlannerState State,
        RecordingRoundPlanRepository Repository);

    private sealed class StubPlannerService(GacPlannerState state) : IGacPlannerService
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

    private sealed class RecordingRoundPlanRepository(GacRoundPlan plan) : IGacRoundPlanRepository
    {
        public GacRoundPlan Plan { get; private set; } = plan;

        public Task<GacRoundPlan?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<GacRoundPlan?>(id == Plan.Id ? Plan : null);

        public Task<bool> TrySaveAsync(
            GacRoundPlan value,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            Plan = value;
            return Task.FromResult(true);
        }

        public Task UpsertAsync(GacRoundPlan value, CancellationToken cancellationToken = default)
        {
            Plan = value;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPersonalBattleRepository : IGacPersonalBattleRepository
    {
        public List<GacPersonalBattleObservation> Items { get; } = [];

        public Task<GacPersonalBattleObservation?> FindByIdAsync(
            string id,
            CancellationToken cancellationToken = default) => Task.FromResult<GacPersonalBattleObservation?>(null);

        public Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetAsync(
            long playerAllyCode,
            GacFormat format,
            int limit = 1_000,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacPersonalBattleObservation>>([]);

        public Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetRoundAsync(
            long playerAllyCode,
            string eventInstanceId,
            int roundNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacPersonalBattleObservation>>([]);

        public Task UpsertAsync(
            GacPersonalBattleObservation observation,
            CancellationToken cancellationToken = default)
        {
            Items.Add(observation);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingPersonalBattleRepository : IGacPersonalBattleRepository
    {
        public Task<GacPersonalBattleObservation?> FindByIdAsync(
            string id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetAsync(
            long playerAllyCode,
            GacFormat format,
            int limit = 1_000,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetRoundAsync(
            long playerAllyCode,
            string eventInstanceId,
            int roundNumber,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpsertAsync(
            GacPersonalBattleObservation observation,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("learning unavailable"));

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubOptimizer(GacAttackOptimizationLookup lookup) : IGacAttackPlanOptimizerService
    {
        public int Calls { get; private set; }

        public Task<GacAttackOptimizationLookup> OptimizeCurrentAsync(
            long allyCode,
            GacAttackOptimizationMode mode,
            bool apply,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(lookup);
        }
    }

    private sealed class ThrowingOptimizer : IGacAttackPlanOptimizerService
    {
        public Task<GacAttackOptimizationLookup> OptimizeCurrentAsync(
            long allyCode,
            GacAttackOptimizationMode mode,
            bool apply,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GacAttackOptimizationLookup>(new InvalidOperationException("optimizer unavailable"));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
