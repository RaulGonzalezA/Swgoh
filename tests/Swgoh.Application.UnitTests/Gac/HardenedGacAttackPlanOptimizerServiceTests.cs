using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class HardenedGacAttackPlanOptimizerServiceTests
{
    private const long PlayerAllyCode = 123_456_789;
    private const long OpponentAllyCode = 987_654_321;

    [Fact]
    public async Task OptimizeCurrentAsync_RebuildWithNoCandidates_RemovesStalePlannedAttacks()
    {
        DateTimeOffset now = new(2026, 9, 18, 8, 30, 0, TimeSpan.Zero);
        Guid presetId = Guid.NewGuid();
        Guid defenseId = Guid.NewGuid();
        Guid attackId = Guid.NewGuid();

        GacPlannerSquad ownSquad = GacPlannerSquad.Create(
            GacFormat.ThreeVsThree,
            "A1",
            ["A2", "A3"],
            isFleet: false);
        GacPlannerSquad enemySquad = GacPlannerSquad.Create(
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
            [GacVisibleDefense.Create(defenseId, "Sur frontal", "Enemy", enemySquad)],
            [GacAttackAssignment.Create(
                attackId,
                defenseId,
                presetId,
                1,
                GacAttackPlanStatus.Planned,
                "stale")],
            now);

        GacTeamPresetDetails unavailablePreset = new(
            presetId,
            PlayerAllyCode,
            "No longer available for offense",
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Defense,
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
            [unavailablePreset],
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
                    "Enemy",
                    SquadDetails("D1", "D2", "D3"),
                    Defeated: false)],
                [new GacAttackAssignmentDetails(
                    attackId,
                    defenseId,
                    unavailablePreset,
                    1,
                    GacAttackPlanStatus.Planned,
                    "stale")],
                [],
                [],
                now));

        var repository = new RecordingPlanRepository(plan);
        var planner = new StubPlannerService(state);
        var clock = new FixedClock(now.AddMinutes(1));
        var inner = new GacAttackPlanOptimizerService(planner, repository, clock);
        using var coordinator = new GacOptimizationCoordinator();
        var service = new HardenedGacAttackPlanOptimizerService(
            inner,
            planner,
            repository,
            new StubLifecycleService(),
            coordinator,
            clock);

        GacAttackOptimizationLookup lookup = await service.OptimizeCurrentAsync(
            PlayerAllyCode,
            GacAttackOptimizationMode.RebuildPlanned,
            apply: true,
            TestContext.Current.CancellationToken);

        Assert.True(lookup.Optimization?.Applied);
        Assert.Empty(repository.Plan.Attacks);
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

    private sealed class RecordingPlanRepository(GacRoundPlan plan) : IGacRoundPlanRepository
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

    private sealed class StubLifecycleService : IGacGeneratedTeamLifecycleService
    {
        public Task<IReadOnlySet<Guid>> GetGeneratedPresetIdsAsync(
            long allyCode,
            GacFormat format,
            GacGeneratedTeamOrigin? origin = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task RegisterAsync(
            long allyCode,
            GacFormat format,
            GacGeneratedTeamOrigin origin,
            string generationId,
            string roundPlanId,
            IReadOnlyCollection<Guid> presetIds,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> PruneUnreferencedAsync(
            long allyCode,
            GacPlannerState state,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task ForgetAsync(
            IReadOnlyCollection<Guid> presetIds,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
