using Swgoh.Application.Gac;
using Swgoh.Application.Players;
using Swgoh.Application.Squads;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Squads;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class OptimizedGacRosterDefenseCandidateProviderTests
{
    private const long AllyCode = 123_456_789;

    [Fact]
    public async Task BuildAsync_PrefersBestJointCompositionOverStrongestSingleTeam()
    {
        PlayerRosterUnit[] units =
        [
            Unit("A", 250_000),
            Unit("B", 250_000),
            Unit("S1", 100_000), Unit("S2", 100_000), Unit("S3", 100_000),
            Unit("F", 100_000), Unit("G", 100_000), Unit("H", 100_000), Unit("I", 100_000),
            Unit("J", 100_000), Unit("K", 100_000), Unit("L", 100_000), Unit("M", 100_000)
        ];
        SquadDefinition strong = Definition(
            "Strong local maximum",
            "A",
            "B", "S1", "S2", "S3");
        SquadDefinition left = Definition(
            "Left wall",
            "A",
            "F", "G", "H", "I");
        SquadDefinition right = Definition(
            "Right wall",
            "B",
            "J", "K", "L", "M");
        var provider = new OptimizedGacRosterDefenseCandidateProvider(
            new FakeRosterService(Snapshot(units)),
            new FakeSquadRepository([strong, left, right]));
        GacDefenseStrategyProfile profile = Profile(
            new GacDefenseTemplateSlot(1, "Norte frontal", null),
            new GacDefenseTemplateSlot(2, "Sur frontal", null));

        GacRosterDefenseCandidateSet result = await provider.BuildAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            profile,
            [],
            battlePlan: null,
            blockedUnitIds: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(result.Candidates, candidate => candidate.Name.Contains("Left wall", StringComparison.Ordinal));
        Assert.Contains(result.Candidates, candidate => candidate.Name.Contains("Right wall", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Candidates,
            candidate => candidate.Name.Contains("Strong local maximum", StringComparison.Ordinal));

        string[] selectedUnits =
        [
            .. result.Candidates
                .SelectMany(candidate => candidate.Squad.AllUnits)
                .Select(unit => unit.DefinitionId)
        ];
        Assert.Equal(
            selectedUnits.Length,
            selectedUnits.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("optimización conjunta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_UsesExistingPresetAndGeneratesOnlyTheBestComplement()
    {
        PlayerRosterUnit[] units =
        [
            Unit("E1", 200_000), Unit("E2", 190_000), Unit("E3", 180_000), Unit("E4", 170_000), Unit("E5", 160_000),
            Unit("N1", 150_000), Unit("N2", 140_000), Unit("N3", 130_000), Unit("N4", 120_000), Unit("N5", 110_000),
            Unit("X1", 90_000), Unit("X2", 80_000), Unit("X3", 70_000), Unit("X4", 60_000), Unit("X5", 50_000)
        ];
        GacTeamPresetDetails existing = Preset(
            "Existing wall",
            units[0],
            units[1],
            units[2],
            units[3],
            units[4]);
        SquadDefinition newWall = Definition(
            "New wall",
            "N1",
            "N2", "N3", "N4", "N5");
        var provider = new OptimizedGacRosterDefenseCandidateProvider(
            new FakeRosterService(Snapshot(units)),
            new FakeSquadRepository([newWall]));
        GacDefenseStrategyProfile profile = Profile(
            new GacDefenseTemplateSlot(1, "Norte frontal", null),
            new GacDefenseTemplateSlot(2, "Sur frontal", null));

        GacRosterDefenseCandidateSet result = await provider.BuildAsync(
            AllyCode,
            GacFormat.FiveVsFive,
            profile,
            [existing],
            battlePlan: null,
            blockedUnitIds: null,
            TestContext.Current.CancellationToken);

        GacTeamPresetDetails generated = Assert.Single(result.Candidates);
        Assert.Contains("New wall", generated.Name, StringComparison.Ordinal);
        Assert.DoesNotContain(
            generated.Squad.AllUnits,
            unit => existing.Squad.AllUnits.Any(existingUnit =>
                existingUnit.DefinitionId.Equals(unit.DefinitionId, StringComparison.OrdinalIgnoreCase)));
    }

    private static GacDefenseStrategyProfile Profile(params GacDefenseTemplateSlot[] slots) => new(
        AllyCode,
        GacFormat.FiveVsFive,
        slots,
        [],
        DateTimeOffset.UtcNow);

    private static SquadDefinition Definition(
        string name,
        string leader,
        params string[] members)
    {
        SquadVariant variant = SquadVariant.Create(
            SquadFormat.FiveVsFive,
            name.ToLowerInvariant().Replace(' ', '-'),
            "Main",
            leader,
            members);
        return SquadDefinition.Create(
            Guid.NewGuid(),
            name,
            SquadFormat.FiveVsFive,
            SquadUse.Defense,
            ["gac"],
            [variant],
            DateTimeOffset.UtcNow);
    }

    private static GacTeamPresetDetails Preset(
        string name,
        PlayerRosterUnit leader,
        params PlayerRosterUnit[] members) => new(
        Guid.NewGuid(),
        AllyCode,
        name,
        GacFormat.FiveVsFive,
        GacPlannerTeamUse.Defense,
        new GacPlannerSquadDetails(
            Details(leader),
            [.. members.Select(Details)],
            IsFleet: false),
        DateTimeOffset.UtcNow);

    private static GacPlannerUnitDetails Details(PlayerRosterUnit unit) => new(
        unit.DefinitionId,
        unit.Name,
        unit.ThumbnailName,
        IsShip: false,
        unit.GalacticPower,
        unit.RelicTier,
        unit.ZetaCount,
        unit.OmicronCount,
        unit.Stats,
        unit.Mods);

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
