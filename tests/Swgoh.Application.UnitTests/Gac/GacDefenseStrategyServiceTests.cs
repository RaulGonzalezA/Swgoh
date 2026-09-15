using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacDefenseStrategyServiceTests
{
    [Fact]
    public void Generate_ExcludesAttackReservationsAndPrefersDefenseTeams()
    {
        GacTeamPresetDetails defense = Preset("Defense A", GacPlannerTeamUse.Defense, 100_000, "A");
        GacTeamPresetDetails reserved = Preset("Defense reserved", GacPlannerTeamUse.Defense, 120_000, "B");
        GacTeamPresetDetails flexible = Preset("Flexible C", GacPlannerTeamUse.Flexible, 90_000, "C");
        GacDefenseStrategyProfile profile = Profile(
            [new(1, "Sur frontal", null), new(2, "Norte frontal", null)],
            [reserved.Id]);

        GacDefenseStrategyService.Generation result = GacDefenseStrategyService.Generate(
            profile,
            [defense, reserved, flexible]);

        Assert.Collection(
            result.Assignments.OrderBy(item => item.Position),
            first => Assert.Equal(defense.Id, first.TeamPresetId),
            second => Assert.Equal(flexible.Id, second.TeamPresetId));
        Assert.DoesNotContain(result.Assignments, item => item.TeamPresetId == reserved.Id);
    }

    [Fact]
    public void Generate_RespectsPinnedTeamAndAvoidsUnitOverlap()
    {
        GacTeamPresetDetails pinned = Preset("Pinned", GacPlannerTeamUse.Defense, 100_000, "A");
        GacTeamPresetDetails overlapping = Preset(
            "Overlap",
            GacPlannerTeamUse.Defense,
            150_000,
            "B",
            sharedMember: pinned.Squad.Members.First().DefinitionId);
        GacTeamPresetDetails safe = Preset("Safe", GacPlannerTeamUse.Flexible, 80_000, "C");
        GacDefenseStrategyProfile profile = Profile(
            [new(1, "Sur frontal", pinned.Id), new(2, "Norte frontal", null)],
            []);

        GacDefenseStrategyService.Generation result = GacDefenseStrategyService.Generate(
            profile,
            [pinned, overlapping, safe]);

        Assert.Collection(
            result.Assignments.OrderBy(item => item.Position),
            first =>
            {
                Assert.Equal(pinned.Id, first.TeamPresetId);
                Assert.True(first.Pinned);
            },
            second => Assert.Equal(safe.Id, second.TeamPresetId));
    }

    private static GacDefenseStrategyProfile Profile(
        IReadOnlyCollection<GacDefenseTemplateSlot> slots,
        IReadOnlyCollection<Guid> reserved) => new(
        123_456_789,
        GacFormat.FiveVsFive,
        slots,
        reserved,
        DateTimeOffset.UtcNow);

    private static GacTeamPresetDetails Preset(
        string name,
        GacPlannerTeamUse use,
        long power,
        string seed,
        string? sharedMember = null)
    {
        long unitPower = power / 5;
        GacPlannerUnitDetails leader = Unit($"{seed}-L", unitPower);
        GacPlannerUnitDetails[] members =
        [
            Unit(sharedMember ?? $"{seed}-1", unitPower),
            Unit($"{seed}-2", unitPower),
            Unit($"{seed}-3", unitPower),
            Unit($"{seed}-4", unitPower)
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

    private static GacPlannerUnitDetails Unit(string id, long power) => new(
        id,
        id,
        ThumbnailName: null,
        IsShip: false,
        GalacticPower: power,
        RelicTier: 7,
        ZetaCount: 1,
        OmicronCount: 0);
}
