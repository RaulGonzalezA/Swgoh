using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacSmartDefenseServiceTests
{
    [Fact]
    public void Generate_PreservesWarRoomCounterEvenWhenItHasMorePower()
    {
        GacTeamPresetDetails expensive = Preset(
            "Expensive flex",
            GacPlannerTeamUse.Flexible,
            150_000,
            "A");
        GacTeamPresetDetails defense = Preset(
            "Cheaper flex",
            GacPlannerTeamUse.Flexible,
            100_000,
            "B");
        GacDefenseStrategyProfile profile = Profile([new(1, "Sur frontal", null)]);
        GacBattleUnit reservedUnit = BattleUnit(expensive.Squad.Leader.DefinitionId);
        CurrentGacBattlePlan battlePlan = BattlePlan(
            [new GacBattleAttackReserve(reservedUnit, "GacSpecialist", "High", "Preserve")],
            [new GacBattleCounterSuggestion(
                BattleUnit("ENEMY"),
                [reservedUnit],
                "High",
                "HistoricalCounterPattern",
                "Observed counter",
                false,
                [reservedUnit])]);

        GacSmartDefenseService.SmartGeneration result = GacSmartDefenseService.Generate(
            profile,
            [expensive, defense],
            Scouting(battlePlan: battlePlan),
            [],
            []);

        GacSmartDefenseAssignment selected = Assert.Single(result.Assignments);
        Assert.Equal(defense.Id, selected.TeamPresetId);
        Assert.Equal(3m, selected.OffensiveOpportunityCost);
    }

    [Fact]
    public void Generate_UsesOpponentCounterHistoryToPreferHarderDefense()
    {
        GacTeamPresetDetails alphabetical = Preset(
            "Alpha",
            GacPlannerTeamUse.Flexible,
            100_000,
            "A");
        GacTeamPresetDetails historicalWall = Preset(
            "Beta",
            GacPlannerTeamUse.Flexible,
            100_000,
            "B");
        GacDefenseStrategyProfile profile = Profile([new(1, "Sur frontal", null)]);
        GacCounterPatternDetails pattern = new(
            IsFleet: false,
            DefenderLeader: ScoutingUnit(historicalWall.Squad.Leader.DefinitionId),
            AttackerLeader: ScoutingUnit("OPPONENT-ATTACKER"),
            Uses: 5,
            Wins: 1,
            WinRate: 20m,
            OneShots: 1,
            OneShotRate: 20m,
            AverageBanners: 52m,
            AverageAttempt: 1.4m);

        GacSmartDefenseService.SmartGeneration result = GacSmartDefenseService.Generate(
            profile,
            [alphabetical, historicalWall],
            Scouting(history: OpponentHistory([pattern])),
            [],
            []);

        GacSmartDefenseAssignment selected = Assert.Single(result.Assignments);
        Assert.Equal(historicalWall.Id, selected.TeamPresetId);
        Assert.Equal(5, selected.OpponentSamples);
        Assert.Equal("High", selected.Confidence);
        Assert.Contains(selected.Reasons, reason => reason.Contains("Histórico del rival", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_UsesEligibleDatacronTierAsDefensiveSignal()
    {
        GacTeamPresetDetails datacronReady = Preset(
            "Datacron ready",
            GacPlannerTeamUse.Flexible,
            100_000,
            "A",
            relicTier: 7);
        GacTeamPresetDetails notEligible = Preset(
            "No datacron",
            GacPlannerTeamUse.Flexible,
            100_000,
            "B",
            relicTier: 3);
        GacDefenseStrategyProfile profile = Profile([new(1, "Sur frontal", null)]);
        GacPlannerDatacronDetails datacron = new(
            "dc-1",
            "set-1",
            "template-1",
            Tier: 9,
            Locked: false,
            HighestRequiredRelicTier: 5,
            HasAbilityAffix: true,
            Affixes: []);

        GacSmartDefenseService.SmartGeneration result = GacSmartDefenseService.Generate(
            profile,
            [datacronReady, notEligible],
            Scouting(),
            [],
            [datacron]);

        GacSmartDefenseAssignment selected = Assert.Single(result.Assignments);
        Assert.Equal(datacronReady.Id, selected.TeamPresetId);
        Assert.Equal(9, selected.EligibleDatacronTier);
        Assert.Contains(selected.Reasons, reason => reason.Contains("Datacron", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_PenalizesTeamWithStrongPersonalOffenseHistory()
    {
        GacTeamPresetDetails provenOffense = Preset(
            "Proven offense",
            GacPlannerTeamUse.Flexible,
            110_000,
            "A");
        GacTeamPresetDetails defense = Preset(
            "Defense alternative",
            GacPlannerTeamUse.Flexible,
            100_000,
            "B");
        GacDefenseStrategyProfile profile = Profile([new(1, "Sur frontal", null)]);
        string[] attackerIds = [.. provenOffense.Squad.AllUnits.Select(unit => unit.DefinitionId)];
        GacPersonalMatchupStatistics personal = new(
            "personal",
            IsFleet: false,
            attackerIds,
            ["X", "Y", "Z", "Q", "R"],
            Uses: 5,
            Wins: 5,
            WinRate: 1m,
            OneShotRate: 1m,
            AverageBanners: 65m,
            LastSeenAtUtc: DateTimeOffset.UtcNow);

        GacSmartDefenseService.SmartGeneration result = GacSmartDefenseService.Generate(
            profile,
            [provenOffense, defense],
            Scouting(),
            [personal],
            []);

        GacSmartDefenseAssignment selected = Assert.Single(result.Assignments);
        Assert.Equal(defense.Id, selected.TeamPresetId);
    }

    private static GacDefenseStrategyProfile Profile(IReadOnlyCollection<GacDefenseTemplateSlot> slots) => new(
        123_456_789,
        GacFormat.FiveVsFive,
        slots,
        [],
        DateTimeOffset.UtcNow);

    private static GacTeamPresetDetails Preset(
        string name,
        GacPlannerTeamUse use,
        long power,
        string seed,
        int relicTier = 7)
    {
        long unitPower = power / 5;
        GacPlannerUnitDetails leader = Unit($"{seed}-L", unitPower, relicTier);
        GacPlannerUnitDetails[] members =
        [
            Unit($"{seed}-1", unitPower, relicTier),
            Unit($"{seed}-2", unitPower, relicTier),
            Unit($"{seed}-3", unitPower, relicTier),
            Unit($"{seed}-4", unitPower, relicTier)
        ];
        return new GacTeamPresetDetails(
            Guid.NewGuid(),
            123_456_789,
            name,
            GacFormat.FiveVsFive,
            use,
            new GacPlannerSquadDetails(leader, members, IsFleet: false),
            DateTimeOffset.UtcNow);
    }

    private static GacPlannerUnitDetails Unit(string id, long power, int relicTier) => new(
        id,
        id,
        ThumbnailName: null,
        IsShip: false,
        GalacticPower: power,
        RelicTier: relicTier,
        ZetaCount: 1,
        OmicronCount: 0);

    private static GacBattleUnit BattleUnit(string id) => new(
        id,
        id,
        GalacticPower: 30_000,
        RelicTier: 7,
        ZetaCount: 1,
        OmicronCount: 0,
        IsShip: false,
        IsGalacticLegend: false);

    private static ScoutingUnitDetails ScoutingUnit(string id) => new(id, id, IsGalacticLegend: false);

    private static CurrentGacBattlePlan BattlePlan(
        IReadOnlyCollection<GacBattleAttackReserve>? reserves = null,
        IReadOnlyCollection<GacBattleCounterSuggestion>? counters = null) => new(
        new GacBattleRosterComparison(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        [],
        [],
        reserves ?? [],
        counters ?? [],
        []);

    private static OpponentScoutingReport OpponentHistory(
        IReadOnlyCollection<GacCounterPatternDetails> counters) => new(
        987_654_321,
        GacFormat.FiveVsFive,
        RoundsAnalyzed: 10,
        SeasonsAnalyzed: 2,
        EarliestRoundUtc: DateTimeOffset.UtcNow.AddDays(-60),
        LatestRoundUtc: DateTimeOffset.UtcNow.AddDays(-1),
        LatestObservedLeague: GacLeague.Kyber,
        TargetLeague: GacLeague.Kyber,
        RequiredSquadDefenses: 9,
        RequiredFleetDefenses: 3,
        AdditionalUnobservedSquadSlots: 0,
        AdditionalUnobservedFleetSlots: 0,
        FullClearRate: 0.6m,
        AverageFirstAttackDelayMinutes: 15m,
        DefensePatterns: [],
        CounterPatterns: counters,
        PredictedSquadDefenses: [],
        PredictedFleetDefenses: []);

    private static CurrentGacScoutingResult Scouting(
        CurrentGacBattlePlan? battlePlan = null,
        OpponentScoutingReport? history = null) => new(
        CurrentGacOpponentLookup.Unavailable(
            CurrentGacOpponentStatus.OpponentUnavailable,
            "test"),
        history,
        RosterScouting: null,
        BattlePlan: battlePlan);
}
