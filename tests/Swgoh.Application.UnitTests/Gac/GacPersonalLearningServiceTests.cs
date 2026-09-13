using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacPersonalLearningServiceTests
{
    [Fact]
    public async Task SyncRoundPlanAsync_CapturesWonAttack()
    {
        DateTimeOffset now = new(2026, 9, 13, 18, 30, 0, TimeSpan.Zero);
        var repository = new FakeRepository();
        var service = new GacPersonalLearningService(repository, new FixedClock(now));
        GacTeamPreset preset = GacTeamPreset.Create(
            Guid.NewGuid(), 123_456_789, "Attack", GacFormat.ThreeVsThree, GacPlannerTeamUse.Offense,
            GacPlannerSquad.Create(GacFormat.ThreeVsThree, "A", ["B", "C"], false), now);
        Guid defenseId = Guid.NewGuid();
        GacRoundPlan plan = GacRoundPlan.Create(
            123_456_789, 987_654_321, "event", "instance", 1,
            GacFormat.ThreeVsThree, GacLeague.Kyber, now);
        GacVisibleDefense defense = GacVisibleDefense.Create(
            defenseId, "Sur frontal", null,
            GacPlannerSquad.Create(GacFormat.ThreeVsThree, "X", ["Y", "Z"], false));
        plan.Replace([], [defense], [GacAttackAssignment.Create(
            Guid.NewGuid(), defenseId, preset.Id, 1, GacAttackPlanStatus.Won, null)], now);

        await service.SyncRoundPlanAsync(plan, new Dictionary<Guid, GacTeamPreset> { [preset.Id] = preset });

        Assert.True(Assert.Single(repository.Items).Won);
    }

    private sealed class FakeRepository : IGacPersonalBattleRepository
    {
        public List<GacPersonalBattleObservation> Items { get; } = [];
        public Task<GacPersonalBattleObservation?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        public Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetAsync(long code, GacFormat format, int limit = 1000, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacPersonalBattleObservation>>([.. Items.Take(limit)]);
        public Task<IReadOnlyCollection<GacPersonalBattleObservation>> GetRoundAsync(long code, string eventId, int round, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<GacPersonalBattleObservation>>([.. Items]);
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

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
