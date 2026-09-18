using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacAttackExecutionServiceTests
{
    private const long PlayerAllyCode = 123_456_789;
    private const long OpponentAllyCode = 987_654_321;

    [Fact]
    public async Task ExecuteAsync_WinPersistsBannersAndReoptimizes()
    {
        DateTimeOffset now = new(2026, 9, 14, 7, 0, 0, TimeSpan.Zero);
        Scenario scenario = BuildScenario(now);
        GacPlannerState wonState = WithAttackStatus(scenario.State, GacAttackPlanStatus.Won, banners: 58);
        var optimizer = new FakeOptimizer(new GacAttackOptimizationLookup(
            CurrentGacOpponentStatus.Found,
            null,
            wonState,
            EmptyOptimization()));
        var personalRepository = new FakePersonalBattleRepository();
        var service = new GacAttackExecutionService(
            new FakePlannerService(scenario.State),
            scenario.PlanRepository,
            personalRepository,
            optimizer,
            new FakeClock(now.AddMinutes(1)));

        GacAttackExecutionLookup result = await service.ExecuteAsync(
            PlayerAllyCode,
            scenario.AttackId,
            new ExecuteGacAttackResult(GacAttackPlanStatus.Won, 58, "Clean one-shot"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.NotNull(result.Execution);
        Assert.Equal(GacAttackPlanStatus.Won, result.Execution!.Status);
        Assert.Equal(58, result.Execution.Banners);
        GacAttackAssignment persistedAttack = Assert.Single(scenario.PlanRepository.Plan.Attacks);
        Assert.Equal(GacAttackPlanStatus.Won, persistedAttack.Status);
        Assert.Equal("Clean one-shot", persistedAttack.Notes);

        GacPersonalBattleObservation observation = Assert.Single(personalRepository.Items);
        Assert.True(observation.Won);
        Assert.Equal(58, observation.Banners);
        Assert.Equal(scenario.AttackId, observation.AttackId);
        Assert.Equal(["A1", "A2", "A3"], observation.AttackerDefinitionIds);
        Assert.Equal(["D1", "D2", "D3"], observation.DefenderDefinitionIds);

        Assert.Equal(1, optimizer.Calls);
        Assert.Equal(GacAttackOptimizationMode.RebuildPlanned, optimizer.LastMode);
        Assert.True(optimizer.LastApply);
        Assert.NotNull(result.Execution.Replan);
    }

    [Fact]
    public async Task ExecuteAsync_FailureConsumesAttackAndReturnsRecalculatedNextRecommendation()
    {
        DateTimeOffset now = new(2026, 9, 14, 7, 10, 0, TimeSpan.Zero);
        Scenario scenario = BuildScenario(now);
        Guid backupPresetId = Guid.NewGuid();
        GacPlannerState failedState = WithAttackStatus(scenario.State, GacAttackPlanStatus.Failed, banners: 12);
        GacAttackOptimizationRecommendation next = new(
            scenario.DefenseId,
            "D1",
            "Sur frontal",
            backupPresetId,
            "Backup",
            82m,
            8m,
            "Personal + histórico",
            "High",
            "El primer equipo quedó consumido; usa el backup.",
            0.8m,
            0.7m,
            55m,
            10);
        GacAttackOptimizationResult optimization = new(
            GacAttackOptimizationMode.RebuildPlanned,
            Applied: true,
            TargetDefenses: 1,
            RecommendedAttacks: 1,
            HistoricalMatches: 1,
            AverageScore: 82m,
            KnownAverageBanners: 55m,
            UncoveredDefenseIds: [],
            Recommendations: [next],
            SearchLimitReached: false);
        var optimizer = new FakeOptimizer(new GacAttackOptimizationLookup(
            CurrentGacOpponentStatus.Found,
            null,
            failedState,
            optimization));
        var personalRepository = new FakePersonalBattleRepository();
        var service = new GacAttackExecutionService(
            new FakePlannerService(scenario.State),
            scenario.PlanRepository,
            personalRepository,
            optimizer,
            new FakeClock(now.AddMinutes(1)));

        GacAttackExecutionLookup result = await service.ExecuteAsync(
            PlayerAllyCode,
            scenario.AttackId,
            new ExecuteGacAttackResult(GacAttackPlanStatus.Failed, 12, "Timeout"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.NotNull(result.Execution);
        Assert.Equal(GacAttackPlanStatus.Failed, scenario.PlanRepository.Plan.Attacks.Single().Status);
        GacPersonalBattleObservation observation = Assert.Single(personalRepository.Items);
        Assert.False(observation.Won);
        Assert.Equal(12, observation.Banners);
        Assert.NotNull(result.Execution!.NextRecommendation);
        Assert.Equal(backupPresetId, result.Execution.NextRecommendation!.TeamPresetId);
        Assert.Equal(scenario.DefenseId, result.Execution.NextRecommendation.DefenseId);
        Assert.Equal(1, optimizer.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNonTerminalExecutionStatus()
    {
        DateTimeOffset now = new(2026, 9, 14, 7, 20, 0, TimeSpan.Zero);
        Scenario scenario = BuildScenario(now);
        var service = new GacAttackExecutionService(
            new FakePlannerService(scenario.State),
            scenario.PlanRepository,
            new FakePersonalBattleRepository(),
            new FakeOptimizer(new GacAttackOptimizationLookup(
                CurrentGacOpponentStatus.Found,
                null,
                scenario.State,
                EmptyOptimization())),
            new FakeClock(now));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(
            PlayerAllyCode,
            scenario.AttackId,
            new ExecuteGacAttackResult(GacAttackPlanStatus.Planned, null, null),
            TestContext.Current.CancellationToken));
    }

    private static Scenario BuildScenario(DateTimeOffset now)
    {
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
        GacVisibleDefense defense = GacVisibleDefense.Create(defenseId, "Sur frontal", "D1", defenseSquad);
        GacAttackAssignment attack = GacAttackAssignment.Create(
            attackId,
            defenseId,
            presetId,
            1,
            GacAttackPlanStatus.Planned,
            "Optimizer note");
        GacRoundPlan plan = GacRoundPlan.Create(
            PlayerAllyCode,
            OpponentAllyCode,
            "event",
            "instance",
            1,
            GacFormat.ThreeVsThree,
            GacLeague.Kyber,
            now);
        plan.Replace([], [defense], [attack], now);
        var planRepository = new FakeRoundPlanRepository(plan);

        GacTeamPresetDetails preset = new(
            presetId,
            PlayerAllyCode,
            "Attack",
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Offense,
            SquadDetails("A1", "A2", "A3"),
            now);
        GacVisibleDefenseDetails defenseDetails = new(
            defenseId,
            "Sur frontal",
            "D1",
            SquadDetails("D1", "D2", "D3"),
            Defeated: false);
        GacAttackAssignmentDetails attackDetails = new(
            attackId,
            defenseId,
            preset,
            1,
            GacAttackPlanStatus.Planned,
            "Optimizer note");
        GacRoundPlanDetails planDetails = new(
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
            [defenseDetails],
            [attackDetails],
            [],
            [],
            now);
        CurrentGacOpponent opponent = new(
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
            "test");
        GacPlannerState state = new(opponent, [preset], planDetails);
        return new Scenario(attackId, defenseId, state, planRepository);
    }

    private static GacPlannerState WithAttackStatus(
        GacPlannerState state,
        GacAttackPlanStatus status,
        int? banners)
    {
        GacAttackAssignmentDetails attack = state.Plan.Attacks.Single() with
        {
            Status = status,
            Banners = banners
        };
        GacVisibleDefenseDetails defense = state.Plan.VisibleDefenses.Single() with
        {
            Defeated = status == GacAttackPlanStatus.Won
        };
        return state with
        {
            Plan = state.Plan with
            {
                Attacks = [attack],
                VisibleDefenses = [defense]
            }
        };
    }

    private static GacPlannerSquadDetails SquadDetails(string leader, params string[] members) => new(
        UnitDetails(leader),
        [.. members.Select(UnitDetails)],
        IsFleet: false);

    private static GacPlannerUnitDetails UnitDetails(string id) => new(
        id,
        id,
        null,
        IsShip: false,
        GalacticPower: 50_000,
        RelicTier: 7,
        ZetaCount: 1,
        OmicronCount: 0);

    private static GacAttackOptimizationResult EmptyOptimization() => new(
        GacAttackOptimizationMode.RebuildPlanned,
        Applied: true,
        TargetDefenses: 0,
        RecommendedAttacks: 0,
        HistoricalMatches: 0,
        AverageScore: 0m,
        KnownAverageBanners: null,
        UncoveredDefenseIds: [],
        Recommendations: [],
        SearchLimitReached: false);

    private sealed record Scenario(
        Guid AttackId,
        Guid DefenseId,
        GacPlannerState State,
        FakeRoundPlanRepository PlanRepository);

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

    private sealed class FakeRoundPlanRepository(GacRoundPlan plan) : IGacRoundPlanRepository
    {
        public GacRoundPlan Plan { get; private set; } = plan;

        public Task<GacRoundPlan?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<GacRoundPlan?>(id == Plan.Id ? Plan : null);

        public Task UpsertAsync(GacRoundPlan value, CancellationToken cancellationToken = default)
        {
            Plan = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePersonalBattleRepository : IGacPersonalBattleRepository
    {
        public List<GacPersonalBattleObservation> Items { get; } = [];

        public Task<GacPersonalBattleObservation?> FindByIdAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<GacPersonalBattleObservation?>(Items.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetAsync(
            long playerAllyCode,
            GacFormat format,
            int limit = 1_000,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacPersonalBattleObservation>>(
                [.. Items.Where(item => item.PlayerAllyCode == playerAllyCode && item.Format == format).Take(limit)]);

        public Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetRoundAsync(
            long playerAllyCode,
            string eventInstanceId,
            int roundNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacPersonalBattleObservation>>(
                [.. Items.Where(item =>
                    item.PlayerAllyCode == playerAllyCode &&
                    item.EventInstanceId == eventInstanceId &&
                    item.RoundNumber == roundNumber)]);

        public Task UpsertAsync(GacPersonalBattleObservation observation, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(item => item.Id == observation.Id);
            Items.Add(observation);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOptimizer(GacAttackOptimizationLookup lookup) : IGacAttackPlanOptimizerService
    {
        public int Calls { get; private set; }
        public GacAttackOptimizationMode? LastMode { get; private set; }
        public bool LastApply { get; private set; }

        public Task<GacAttackOptimizationLookup> OptimizeCurrentAsync(
            long allyCode,
            GacAttackOptimizationMode mode,
            bool apply,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastMode = mode;
            LastApply = apply;
            return Task.FromResult(lookup);
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
