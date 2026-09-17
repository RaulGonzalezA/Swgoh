using Swgoh.Application.Abstractions;
using Swgoh.Application.Investments;
using Swgoh.Application.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Investments;

public sealed class InvestmentTargetServiceTests
{
    private const long AllyCode = 123_456_789L;
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SaveAsync_WithRelicTarget_PersistsAndCalculatesInventoryGap()
    {
        PlayerRosterUnit unit = CreateUnit(relicTier: 5, rarity: 7);
        var repository = new FakeTargetRepository();
        var service = CreateService(repository, unit, CreateZeroInventory());

        InvestmentTargetProgress result = await service.SaveAsync(
            AllyCode,
            unit.DefinitionId,
            new InvestmentTargetUpdate(7, null),
            CancellationToken.None);

        Assert.Equal(7, result.TargetRelicTier);
        Assert.Equal(2, result.RelicStepsRemaining);
        Assert.False(result.Completed);
        Assert.NotNull(result.Inventory);
        Assert.InRange(result.Inventory.MissingResourceTypes, 1, int.MaxValue);
        Assert.NotNull(await repository.GetAsync(AllyCode, unit.DefinitionId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAllAsync_WhenRosterReachedTarget_MarksTargetCompleted()
    {
        PlayerRosterUnit unit = CreateUnit(relicTier: 7, rarity: 7);
        var repository = new FakeTargetRepository();
        await repository.UpsertAsync(
            new InvestmentTarget(AllyCode, unit.DefinitionId, 7, null, Now.AddDays(-2), Now.AddDays(-2)),
            CancellationToken.None);
        var service = CreateService(repository, unit, inventory: null);

        InvestmentTargetProgress result = Assert.Single(await service.GetAllAsync(AllyCode, CancellationToken.None));

        Assert.True(result.Completed);
        Assert.Equal(1m, result.Progress);
        Assert.Equal(0, result.RelicStepsRemaining);
        Assert.Equal("Objetivo completado", result.SuggestedAction);
    }

    [Fact]
    public async Task SaveAsync_WithRelicTargetForShip_ThrowsArgumentException()
    {
        PlayerRosterUnit ship = CreateUnit(relicTier: 0, rarity: 6, isShip: true);
        var service = CreateService(new FakeTargetRepository(), ship, inventory: null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(
            AllyCode,
            ship.DefinitionId,
            new InvestmentTargetUpdate(5, null),
            CancellationToken.None));
    }

    private static InvestmentTargetService CreateService(
        IInvestmentTargetRepository repository,
        PlayerRosterUnit unit,
        PlayerInventorySnapshot? inventory)
    {
        var snapshot = new PlayerRosterSnapshot(
            AllyCode,
            Now,
            "Tester",
            10_000_000,
            1,
            [unit],
            unit.Factions);
        return new InvestmentTargetService(
            repository,
            new FakeRosterService(snapshot),
            new FakeInventoryService(inventory),
            new FixedClock(Now));
    }

    private static PlayerInventorySnapshot CreateZeroInventory() => new(
        AllyCode,
        Now,
        "test",
        [.. PlayerInventoryCatalog.Resources.Select(resource => new PlayerInventoryResource(resource.Id, resource.Name, 0))]);

    private static PlayerRosterUnit CreateUnit(int relicTier, int rarity, bool isShip = false) => new(
        "unit-1",
        isShip ? "SHIP_TEST" : "CHAR_TEST",
        isShip ? "Test Ship" : "Test Character",
        null,
        null,
        isShip ? ["Fleet"] : ["Galactic Republic"],
        [],
        85,
        rarity,
        isShip ? 0 : 13,
        relicTier,
        6,
        30_000,
        isShip,
        1,
        0);

    private sealed class FakeTargetRepository : IInvestmentTargetRepository
    {
        private readonly Dictionary<string, InvestmentTarget> targets = new(StringComparer.OrdinalIgnoreCase);

        public Task<InvestmentTarget?> GetAsync(long allyCode, string definitionId, CancellationToken cancellationToken = default)
        {
            targets.TryGetValue(Key(allyCode, definitionId), out InvestmentTarget? target);
            return Task.FromResult(target);
        }

        public Task<IReadOnlyCollection<InvestmentTarget>> GetAllAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<InvestmentTarget>>([.. targets.Values.Where(target => target.AllyCode == allyCode)]);

        public Task UpsertAsync(InvestmentTarget target, CancellationToken cancellationToken = default)
        {
            targets[Key(target.AllyCode, target.DefinitionId)] = target;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(long allyCode, string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(targets.Remove(Key(allyCode, definitionId)));

        private static string Key(long allyCode, string definitionId) => $"{allyCode}:{definitionId}";
    }

    private sealed class FakeRosterService(PlayerRosterSnapshot snapshot) : IPlayerRosterService
    {
        public Task<PlayerRosterPage?> GetAsync(
            long allyCode,
            PlayerRosterQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PlayerRosterPage?>(null);

        public Task<PlayerRosterSnapshot?> GetSnapshotAsync(
            long allyCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PlayerRosterSnapshot?>(allyCode == snapshot.AllyCode ? snapshot : null);
    }

    private sealed class FakeInventoryService(PlayerInventorySnapshot? snapshot) : IPlayerInventoryService
    {
        public Task<PlayerInventorySnapshot?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot?.AllyCode == allyCode ? snapshot : null);

        public Task<PlayerInventorySnapshot> ImportAsync(
            long allyCode,
            PlayerInventoryImport inventory,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
