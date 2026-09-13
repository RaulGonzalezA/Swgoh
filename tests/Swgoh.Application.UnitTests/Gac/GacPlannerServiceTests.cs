using Swgoh.Application.Abstractions;
using Swgoh.Application.Gac;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacPlannerServiceTests
{
    private const long PlayerAllyCode = 123456789;
    private const long OpponentAllyCode = 987654321;

    [Fact]
    public async Task SaveCurrentAsync_WhenAttackReusesOwnDefenseUnit_ReturnsConflict()
    {
        var presetRepository = new FakePresetRepository();
        var planRepository = new FakePlanRepository();
        PlayerProfile player = CreatePlayer(
            PlayerAllyCode,
            [
                Unit("A", 30_000),
                Unit("B", 28_000),
                Unit("C", 27_000),
                Unit("D", 26_000),
                Unit("E", 25_000)
            ]);
        var profiles = new FakePlayerProfileService(player, CreatePlayer(
            OpponentAllyCode,
            [Unit("X"), Unit("Y"), Unit("Z")]));
        var service = CreateService(presetRepository, planRepository, profiles);

        GacTeamPresetDetails defense = await service.CreatePresetAsync(
            PlayerAllyCode,
            new SaveGacTeamPreset(
                "Defense A",
                GacFormat.ThreeVsThree,
                GacPlannerTeamUse.Defense,
                "A",
                ["B", "C"],
                IsFleet: false),
            TestContext.Current.CancellationToken);
        GacTeamPresetDetails offense = await service.CreatePresetAsync(
            PlayerAllyCode,
            new SaveGacTeamPreset(
                "Offense A",
                GacFormat.ThreeVsThree,
                GacPlannerTeamUse.Offense,
                "A",
                ["D", "E"],
                IsFleet: false),
            TestContext.Current.CancellationToken);
        Guid visibleDefenseId = Guid.NewGuid();

        GacPlannerLookup result = await service.SaveCurrentAsync(
            PlayerAllyCode,
            new SaveCurrentGacRoundPlan(
                [new SaveGacOwnDefenseAssignment(Guid.NewGuid(), "south-front", defense.Id)],
                [new SaveGacVisibleDefense(
                    visibleDefenseId,
                    "south-front",
                    "Enemy",
                    "X",
                    ["Y", "Z"],
                    IsFleet: false)],
                [new SaveGacAttackAssignment(
                    Guid.NewGuid(),
                    visibleDefenseId,
                    offense.Id,
                    1,
                    GacAttackPlanStatus.Planned,
                    null)]),
            TestContext.Current.CancellationToken);

        GacPlannerState state = Assert.IsType<GacPlannerState>(result.State);
        GacPlannerConflict conflict = Assert.Single(
            state.Plan.Conflicts,
            item => item.Code == "DefenseAttackOverlap");
        Assert.Contains("A", conflict.UnitDefinitionIds);
    }

    [Fact]
    public async Task SaveCurrentAsync_WhenFirstAttemptFails_AllowsSecondAttemptWithoutConflictWhenTeamsDoNotOverlap()
    {
        var presetRepository = new FakePresetRepository();
        var planRepository = new FakePlanRepository();
        PlayerProfile player = CreatePlayer(
            PlayerAllyCode,
            [
                Unit("A"), Unit("B"), Unit("C"),
                Unit("D"), Unit("E"), Unit("F")
            ]);
        var profiles = new FakePlayerProfileService(player, CreatePlayer(
            OpponentAllyCode,
            [Unit("X"), Unit("Y"), Unit("Z")]));
        var service = CreateService(presetRepository, planRepository, profiles);
        GacTeamPresetDetails firstTeam = await service.CreatePresetAsync(
            PlayerAllyCode,
            new SaveGacTeamPreset(
                "First",
                GacFormat.ThreeVsThree,
                GacPlannerTeamUse.Offense,
                "A",
                ["B", "C"],
                false),
            TestContext.Current.CancellationToken);
        GacTeamPresetDetails secondTeam = await service.CreatePresetAsync(
            PlayerAllyCode,
            new SaveGacTeamPreset(
                "Second",
                GacFormat.ThreeVsThree,
                GacPlannerTeamUse.Offense,
                "D",
                ["E", "F"],
                false),
            TestContext.Current.CancellationToken);
        Guid defenseId = Guid.NewGuid();

        GacPlannerLookup result = await service.SaveCurrentAsync(
            PlayerAllyCode,
            new SaveCurrentGacRoundPlan(
                [],
                [new SaveGacVisibleDefense(
                    defenseId,
                    "north-front",
                    null,
                    "X",
                    ["Y", "Z"],
                    false)],
                [
                    new SaveGacAttackAssignment(
                        Guid.NewGuid(),
                        defenseId,
                        firstTeam.Id,
                        1,
                        GacAttackPlanStatus.Failed,
                        null),
                    new SaveGacAttackAssignment(
                        Guid.NewGuid(),
                        defenseId,
                        secondTeam.Id,
                        2,
                        GacAttackPlanStatus.Planned,
                        null)
                ]),
            TestContext.Current.CancellationToken);

        GacPlannerState state = Assert.IsType<GacPlannerState>(result.State);
        Assert.Equal(2, state.Plan.Attacks.Count);
        Assert.DoesNotContain(state.Plan.Conflicts, item => item.Code == "AttackReuse");
    }

    [Fact]
    public async Task CreatePresetAsync_WhenUnitIsNotInRoster_Throws()
    {
        var presetRepository = new FakePresetRepository();
        var planRepository = new FakePlanRepository();
        var profiles = new FakePlayerProfileService(
            CreatePlayer(PlayerAllyCode, [Unit("A"), Unit("B")]),
            CreatePlayer(OpponentAllyCode, []));
        var service = CreateService(presetRepository, planRepository, profiles);

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePresetAsync(
            PlayerAllyCode,
            new SaveGacTeamPreset(
                "Invalid",
                GacFormat.ThreeVsThree,
                GacPlannerTeamUse.Offense,
                "A",
                ["B", "MISSING"],
                false),
            TestContext.Current.CancellationToken));

        Assert.Contains("MISSING", exception.Message, StringComparison.Ordinal);
    }

    private static GacPlannerService CreateService(
        IGacTeamPresetRepository presetRepository,
        IGacRoundPlanRepository planRepository,
        IPlayerProfileService profiles) => new(
            presetRepository,
            planRepository,
            new FakeCurrentScoutingService(),
            profiles,
            new FakeGameDataCatalog(),
            new FixedClock(new DateTimeOffset(2026, 9, 13, 18, 0, 0, TimeSpan.Zero)));

    private static PlayerProfile CreatePlayer(long allyCode, IReadOnlyCollection<RosterUnit> roster) =>
        PlayerProfile.Import(
            allyCode,
            $"player-{allyCode}",
            allyCode == PlayerAllyCode ? "Player" : "Opponent",
            null,
            null,
            85,
            roster.Sum(unit => unit.GalacticPower),
            DateTimeOffset.UtcNow,
            roster);

    private static RosterUnit Unit(string definitionId, long galacticPower = 20_000) => new(
        definitionId,
        definitionId,
        85,
        7,
        13,
        7,
        6,
        galacticPower,
        IsShip: false,
        ZetaCount: 1,
        OmicronCount: 0);

    private sealed class FakePresetRepository : IGacTeamPresetRepository
    {
        private readonly Dictionary<Guid, GacTeamPreset> presets = [];

        public Task<GacTeamPreset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(presets.GetValueOrDefault(id));

        public Task<IReadOnlyCollection<GacTeamPreset>> GetAsync(
            long allyCode,
            GacFormat? format,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<GacTeamPreset> result =
            [
                .. presets.Values.Where(preset =>
                    preset.AllyCode == allyCode &&
                    (format is null || preset.Format == format))
            ];
            return Task.FromResult(result);
        }

        public Task UpsertAsync(GacTeamPreset preset, CancellationToken cancellationToken = default)
        {
            presets[preset.Id] = preset;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(presets.Remove(id));
    }

    private sealed class FakePlanRepository : IGacRoundPlanRepository
    {
        private readonly Dictionary<string, GacRoundPlan> plans = [];

        public Task<GacRoundPlan?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(plans.GetValueOrDefault(id));

        public Task UpsertAsync(GacRoundPlan plan, CancellationToken cancellationToken = default)
        {
            plans[plan.Id] = plan;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentScoutingService : ICurrentGacScoutingService
    {
        public Task<CurrentGacScoutingResult> GetAsync(
            long allyCode,
            GacFormat? formatOverride,
            int maxRounds,
            CancellationToken cancellationToken = default)
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
            return Task.FromResult(new CurrentGacScoutingResult(
                CurrentGacOpponentLookup.Found(opponent),
                null,
                null,
                null));
        }
    }

    private sealed class FakePlayerProfileService(params PlayerProfile[] profiles) : IPlayerProfileService
    {
        private readonly Dictionary<long, PlayerProfile> players = profiles.ToDictionary(player => player.AllyCode);

        public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(players.GetValueOrDefault(allyCode));

        public Task<PlayerProfile> SaveAsync(
            long allyCode,
            string name,
            long galacticPower,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlayerProfile> RefreshFromGameAsync(
            long allyCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(players[allyCode]);
    }

    private sealed class FakeGameDataCatalog : ISwgohGameDataCatalog
    {
        public Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(
            new GameDataCatalog(
                new Dictionary<string, GameUnitDefinition>(),
                new Dictionary<string, GameSkillDefinition>(),
                []));
    }

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
