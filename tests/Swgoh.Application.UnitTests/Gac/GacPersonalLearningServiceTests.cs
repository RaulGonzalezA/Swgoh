using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacPersonalLearningServiceTests
{
    private const long PlayerAllyCode = 123456789;
    private const long OpponentAllyCode = 987654321;
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task SyncRoundPlanAsync_CapturesWonAttackAndRemovesStaleObservation()
    {
        var repository = new FakeRepository();
        var service = new GacPersonalLearningService(repository, new FixedClock(Now));
        GacTeamPreset preset = GacTeamPreset.Create(
            Guid.NewGuid(),
            PlayerAllyCode,
            "Attack",
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Offense,
            GacPlannerSquad.Create(GacFormat.ThreeVsThree, "A", ["B", "C"], false),
            Now);
        Guid defenseId = Guid.NewGuid();
        Guid attackId = Guid.NewGuid();
        GacRoundPlan plan = CreatePlan(
            defenseId,
            GacAttackAssignment.Create(
                attackId,
                defenseId,
                preset.Id,
                1,
                GacAttackPlanStatus.Won,
                null));

        await service.SyncRoundPlanAsync(
            plan,
            new Dictionary<Guid, GacTeamPreset> { [preset.Id] = preset },
            TestContext.Current.CancellationToken);

        GacPersonalBattleObservation observation = Assert.Single(repository.Items);
        Assert.True(observation.Won);
        Assert.Equal(["A", "B", "C"], observation.AttackerDefinitionIds);
        Assert.Equal(["X", "Y", "Z"], observation.DefenderDefinitionIds);

        plan.Replace(
            [],
            plan.VisibleDefenses,
            [GacAttackAssignment.Create(
                attackId,
                defenseId,
                preset.Id,
                1,
                GacAttackPlanStatus.Planned,
                null)],
            Now);
        await service.SyncRoundPlanAsync(
            plan,
            new Dictionary<Guid, GacTeamPreset> { [preset.Id] = preset },
            TestContext.Current.CancellationToken);

        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task GetStatisticsAsync_IgnoresObservationsOutsideLearningWindow()
    {
        var repository = new FakeRepository();
        repository.Items.Add(Observation(Guid.NewGuid(), won: true, Now.AddDays(-10)));
        repository.Items.Add(Observation(Guid.NewGuid(), won: false, Now.AddDays(-200)));
        var service = new GacPersonalLearningService(repository, new FixedClock(Now));

        IReadOnlyCollection<GacPersonalMatchupStatistics> result = await service.GetStatisticsAsync(
            PlayerAllyCode,
            GacFormat.ThreeVsThree,
            TestContext.Current.CancellationToken);

        GacPersonalMatchupStatistics stats = Assert.Single(result);
        Assert.Equal(1, stats.Uses);
        Assert.Equal(1, stats.Wins);
        Assert.Equal(1m, stats.WinRate);
    }

    private static GacRoundPlan CreatePlan(Guid defenseId, GacAttackAssignment attack)
    {
        GacRoundPlan plan = GacRoundPlan.Create(
            PlayerAllyCode,
            OpponentAllyCode,
            "event",
            "instance",
            1,
            GacFormat.ThreeVsThree,
            GacLeague.Kyber,
            Now);
        GacVisibleDefense defense = GacVisibleDefense.Create(
            defenseId,
            "Sur frontal",
            null,
            GacPlannerSquad.Create(GacFormat.ThreeVsThree, "X", ["Y", "Z"], false));
        plan.Replace([], [defense], [attack], Now);
        return plan;
    }

    private static GacPersonalBattleObservation Observation(Guid attackId, bool won, DateTimeOffset at) =>
        GacPersonalBattleObservation.Create(
            PlayerAllyCode,
            OpponentAllyCode,
            "old-instance",
            1,
            GacFormat.ThreeVsThree,
            attackId,
            Guid.NewGuid(),
            1,
            false,
            ["A", "B", "C"],
            ["X", "Y", "Z"],
            won,
            null,
            at);

    private sealed class FakeRepository : IGacPersonalBattleRepository
    {
        public List<GacPersonalBattleObservation> Items { get; } = [];

        public Task<GacPersonalBattleObservation?> FindByIdAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetAsync(
            long playerAllyCode,
            GacFormat format,
            int limit = 1000,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacPersonalBattleObservation>>(
                [.. Items
                    .Where(item => item.PlayerAllyCode == playerAllyCode && item.Format == format)
                    .OrderByDescending(item => item.RecordedAtUtc)
                    .Take(limit)]);

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

        public Task UpsertAsync(
            GacPersonalBattleObservation observation,
            CancellationToken cancellationToken = default)
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

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
