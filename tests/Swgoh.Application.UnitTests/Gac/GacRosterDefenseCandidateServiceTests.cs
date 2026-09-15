using Swgoh.Application.Gac;
using Swgoh.Application.Players;
using Swgoh.Application.Squads;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Squads;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacRosterDefenseCandidateServiceTests
{
    private const long AllyCode = 123_456_789;

    [Fact]
    public async Task BuildAsync_WithNoPresets_GeneratesDisjointDefenseTeamsFromRoster()
    {
        PlayerRosterUnit[] units =
        [
            .. Enumerable.Range(1, 15).Select(index => Unit($"U{index}", 100_000 - (index * 1_000)))
        ];
        var service = new GacRosterDefenseCandidateService(
            new FakeRosterService(Snapshot(units)),
            new FakeSquadRepository([]));
        GacDefenseStrategyProfile profile = Profile(
            new GacDefenseTemplateSlot(1, "Norte frontal", null),
            new GacDefenseTemplateSlot(2, "Sur frontal", null));

        GacRosterDefenseCandidateSet result = await service.BuildAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            profile,
            [],
            battlePlan: null,
            blockedUnitIds: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Candidates.Count >= 2);
        GacTeamPresetDetails[] firstTwo = [.. result.Candidates.Where(item => !item.Squad.IsFleet).Take(2)];
        Assert.Equal(2, firstTwo.Length);
        string[] used = [.. firstTwo.SelectMany(item => item.Squad.AllUnits).Select(item => item.DefinitionId)];
        Assert.Equal(used.Length, used.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(firstTwo, item => Assert.Equal(GacPlannerTeamUse.Defense, item.Use));
        Assert.Contains(result.Warnings, warning => warning.Contains("automáticamente desde tu roster", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsync_ExcludesWarRoomAttackReserves()
    {
        PlayerRosterUnit reserved = Unit("RESERVED", 200_000);
        PlayerRosterUnit[] units =
        [
            reserved,
            .. Enumerable.Range(1, 12).Select(index => Unit($"U{index}", 100_000 - (index * 1_000)))
        ];
        var service = new GacRosterDefenseCandidateService(
            new FakeRosterService(Snapshot(units)),
            new FakeSquadRepository([]));
        GacDefenseStrategyProfile profile = Profile(new GacDefenseTemplateSlot(1, "Norte frontal", null));
        CurrentGacBattlePlan battlePlan = new(
            new GacBattleRosterComparison(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            [],
            [],
            [new GacBattleAttackReserve(
                new GacBattleUnit(
                    reserved.DefinitionId,
                    reserved.Name,
                    reserved.GalacticPower,
                    reserved.RelicTier,
                    reserved.ZetaCount,
                    reserved.OmicronCount,
                    IsShip: false,
                    IsGalacticLegend: false),
                "GacSpecialist",
                "High",
                "Preserve")],
            [],
            []);

        GacRosterDefenseCandidateSet result = await service.BuildAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            profile,
            [],
            battlePlan,
            blockedUnitIds: null,
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            result.Candidates.SelectMany(item => item.Squad.AllUnits),
            unit => unit.DefinitionId == reserved.DefinitionId);
    }

    [Fact]
    public async Task BuildAsync_PrefersOwnedCuratedSquadBeforeHeuristicFallback()
    {
        PlayerRosterUnit[] units =
        [
            Unit("L", 90_000), Unit("A", 80_000), Unit("B", 70_000), Unit("C", 60_000), Unit("D", 50_000),
            .. Enumerable.Range(1, 10).Select(index => Unit($"X{index}", 40_000 - index))
        ];
        SquadVariant variant = SquadVariant.Create(
            SquadFormat.FiveVsFive,
            "main",
            "Main",
            "L",
            ["A", "B", "C", "D"]);
        SquadDefinition definition = SquadDefinition.Create(
            Guid.NewGuid(),
            "Known defense",
            SquadFormat.FiveVsFive,
            SquadUse.Defense,
            ["gac"],
            [variant],
            DateTimeOffset.UtcNow);
        var service = new GacRosterDefenseCandidateService(
            new FakeRosterService(Snapshot(units)),
            new FakeSquadRepository([definition]));

        GacRosterDefenseCandidateSet result = await service.BuildAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            Profile(new GacDefenseTemplateSlot(1, "Norte frontal", null)),
            [],
            battlePlan: null,
            blockedUnitIds: null,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Candidates, item => item.Name.Contains("Known defense", StringComparison.Ordinal));
    }

    private static GacDefenseStrategyProfile Profile(params GacDefenseTemplateSlot[] slots) => new(
        AllyCode,
        GacFormat.FiveVsFive,
        slots,
        [],
        DateTimeOffset.UtcNow);

    private static PlayerRosterSnapshot Snapshot(IReadOnlyCollection<PlayerRosterUnit> units) => new(
        AllyCode,
        DateTimeOffset.UtcNow,
        "Player",
        units.Sum(unit => unit.GalacticPower),
        units.Count,
        units,
        ["Faction"]);

    private static PlayerRosterUnit Unit(string id, long power) => new(
        id,
        id,
        id,
        NameKey: null,
        ThumbnailName: null,
        Factions: ["Faction"],
        Tags: [],
        Level: 85,
        Rarity: 7,
        GearTier: 13,
        RelicTier: 7,
        EquippedModCount: 6,
        GalacticPower: power,
        IsShip: false,
        ZetaCount: 1,
        OmicronCount: 0);

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
            Task.FromResult<PlayerRosterSnapshot?>(snapshot);
    }

    private sealed class FakeSquadRepository(IReadOnlyCollection<SquadDefinition> squads) : ISquadRepository
    {
        public Task<SquadDefinition?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(squads.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyCollection<SquadDefinition>> SearchAsync(
            SquadSearchQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(squads);

        public Task UpsertAsync(SquadDefinition squad, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
