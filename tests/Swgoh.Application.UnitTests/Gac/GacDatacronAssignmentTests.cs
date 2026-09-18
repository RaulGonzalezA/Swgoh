using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacDatacronAssignmentTests
{
    private const long PlayerAllyCode = 123456789;
    private const long OpponentAllyCode = 987654321;
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 17, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Replace_RejectsDatacronUsedByDefenseAndPlannedAttack()
    {
        GacRoundPlan plan = CreatePlan();
        GacVisibleDefense visible = VisibleDefense();
        const string datacronId = "dc-exclusive";

        ArgumentException exception = Assert.Throws<ArgumentException>(() => plan.Replace(
            [GacOwnDefenseAssignment.Create(Guid.NewGuid(), "Sur frontal", Guid.NewGuid(), datacronId)],
            [visible],
            [GacAttackAssignment.Create(
                Guid.NewGuid(),
                visible.Id,
                Guid.NewGuid(),
                1,
                GacAttackPlanStatus.Planned,
                null,
                datacronId)],
            Now.AddMinutes(1)));

        Assert.Contains("only be assigned once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(GacAttackPlanStatus.Won)]
    [InlineData(GacAttackPlanStatus.Failed)]
    [InlineData(GacAttackPlanStatus.Cancelled)]
    public void Replace_ReleasesDatacronWhenAttackIsNoLongerPlanned(GacAttackPlanStatus status)
    {
        GacRoundPlan plan = CreatePlan();
        GacVisibleDefense visible = VisibleDefense();
        const string datacronId = "dc-released";

        plan.Replace(
            [GacOwnDefenseAssignment.Create(Guid.NewGuid(), "Sur frontal", Guid.NewGuid(), datacronId)],
            [visible],
            [GacAttackAssignment.Create(
                Guid.NewGuid(),
                visible.Id,
                Guid.NewGuid(),
                1,
                status,
                null,
                datacronId)],
            Now.AddMinutes(1));

        Assert.Equal(datacronId, Assert.Single(plan.OwnDefenses).DatacronId);
        Assert.Equal(datacronId, Assert.Single(plan.Attacks).DatacronId);
    }

    [Fact]
    public void AllocateAttackDatacrons_RebuildDoesNotReuseDatacronFromCompletedAttack()
    {
        GacTeamPresetDetails usedTeam = Team(relicTier: 7);
        GacTeamPresetDetails nextTeam = Team(relicTier: 7);
        Guid defenseId = Guid.NewGuid();
        GacPlannerDatacronDetails used = Datacron("dc-used", tier: 9, requiredRelic: 7, hasAbility: true);
        GacPlannerDatacronDetails available = Datacron("dc-free", tier: 6, requiredRelic: 5, hasAbility: false);

        var opponent = new CurrentGacOpponent(
            PlayerAllyCode,
            OpponentAllyCode,
            "Opponent",
            null,
            GacLeague.Kyber,
            GacFormat.FiveVsFive,
            "event",
            "instance",
            "bracket",
            1,
            "test",
            "test");
        var completedAttack = new GacAttackAssignmentDetails(
            Guid.NewGuid(),
            defenseId,
            usedTeam,
            1,
            GacAttackPlanStatus.Won,
            null,
            Banners: 57,
            DatacronId: used.Id);
        var plan = new GacRoundPlanDetails(
            GacRoundPlan.BuildId(PlayerAllyCode, "instance", 1),
            PlayerAllyCode,
            OpponentAllyCode,
            "Opponent",
            "event",
            "instance",
            1,
            GacFormat.FiveVsFive,
            GacLeague.Kyber,
            [],
            [],
            [completedAttack],
            [],
            [],
            Now);
        var state = new GacPlannerState(
            opponent,
            [usedTeam, nextTeam],
            plan,
            [used, available]);
        var recommendation = new GacAttackOptimizationRecommendation(
            defenseId,
            "Enemy",
            "Sur frontal",
            nextTeam.Id,
            nextTeam.Name,
            80m,
            5m,
            "Test",
            "High",
            "Test",
            null,
            null,
            null,
            null);

        IReadOnlyDictionary<Guid, string?> allocation =
            HardenedGacAttackPlanOptimizerService.AllocateAttackDatacrons(
                state,
                [recommendation],
                GacAttackOptimizationMode.RebuildPlanned);

        Assert.Equal(available.Id, allocation[nextTeam.Id]);
    }

    [Fact]
    public void BestEligible_SkipsReservedDatacronAndUsesNextEligibleOne()
    {
        GacTeamPresetDetails team = Team(relicTier: 7);
        GacPlannerDatacronDetails best = Datacron("dc-9", tier: 9, requiredRelic: 7, hasAbility: true);
        GacPlannerDatacronDetails fallback = Datacron("dc-6", tier: 6, requiredRelic: 5, hasAbility: false);
        IReadOnlySet<string> reserved = new HashSet<string>([best.Id], StringComparer.OrdinalIgnoreCase);

        GacPlannerDatacronDetails? selected = GacDatacronRules.BestEligible(
            team,
            [fallback, best],
            reserved,
            Now);

        Assert.NotNull(selected);
        Assert.Equal(fallback.Id, selected.Id);
    }

    [Fact]
    public void BestEligible_RejectsDatacronAboveSquadRelicRequirement()
    {
        GacTeamPresetDetails team = Team(relicTier: 6);
        GacPlannerDatacronDetails unavailable = Datacron("dc-r7", tier: 9, requiredRelic: 7, hasAbility: true);

        GacPlannerDatacronDetails? selected = GacDatacronRules.BestEligible(team, [unavailable], nowUtc: Now);

        Assert.Null(selected);
    }

    [Fact]
    public void BestEligible_SkipsExpiredHigherTierDatacron()
    {
        GacTeamPresetDetails team = Team(relicTier: 7);
        GacPlannerDatacronDetails expired = Datacron(
            "dc-expired",
            tier: 9,
            requiredRelic: 7,
            hasAbility: true,
            expiresAtUtc: Now.AddMinutes(-1));
        GacPlannerDatacronDetails active = Datacron(
            "dc-active",
            tier: 6,
            requiredRelic: 5,
            hasAbility: false,
            expiresAtUtc: Now.AddDays(7));

        GacPlannerDatacronDetails? selected = GacDatacronRules.BestEligible(
            team,
            [expired, active],
            nowUtc: Now);

        Assert.NotNull(selected);
        Assert.Equal(active.Id, selected.Id);
    }

    private static GacTeamPresetDetails Team(int relicTier)
    {
        GacPlannerUnitDetails leader = Unit("L", relicTier);
        GacPlannerUnitDetails[] members =
        [
            Unit("A", relicTier),
            Unit("B", relicTier),
            Unit("C", relicTier),
            Unit("D", relicTier)
        ];
        return new GacTeamPresetDetails(
            Guid.NewGuid(),
            PlayerAllyCode,
            "Equipo",
            GacFormat.FiveVsFive,
            GacPlannerTeamUse.Offense,
            new GacPlannerSquadDetails(leader, members, IsFleet: false),
            Now);
    }

    private static GacPlannerUnitDetails Unit(string id, int relicTier) => new(
        id,
        id,
        null,
        IsShip: false,
        GalacticPower: 20_000,
        RelicTier: relicTier,
        ZetaCount: 0,
        OmicronCount: 0);

    private static GacPlannerDatacronDetails Datacron(
        string id,
        int tier,
        int requiredRelic,
        bool hasAbility,
        DateTimeOffset? expiresAtUtc = null) => new(
        id,
        "set",
        "template",
        tier,
        Locked: false,
        HighestRequiredRelicTier: requiredRelic,
        HasAbilityAffix: hasAbility,
        Affixes: [],
        ExpiresAtUtc: expiresAtUtc);

    private static GacRoundPlan CreatePlan() => GacRoundPlan.Create(
        PlayerAllyCode,
        OpponentAllyCode,
        "event",
        "instance",
        1,
        GacFormat.FiveVsFive,
        GacLeague.Kyber,
        Now);

    private static GacVisibleDefense VisibleDefense() => GacVisibleDefense.Create(
        Guid.NewGuid(),
        "Sur frontal",
        "Rival",
        GacPlannerSquad.Create(
            GacFormat.FiveVsFive,
            "L",
            ["A", "B", "C", "D"],
            isFleet: false));
}
