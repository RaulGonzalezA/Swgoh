using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Domain.UnitTests.Gac;

public sealed class GacPersonalBattleObservationTests
{
    [Fact]
    public void BuildMatchupKey_PreservesLeaderAndNormalizesMemberOrder()
    {
        string first = GacPersonalBattleObservation.BuildMatchupKey(
            GacFormat.ThreeVsThree,
            isFleet: false,
            ["LEADER_A", "MEMBER_C", "MEMBER_B"],
            ["LEADER_X", "MEMBER_Z", "MEMBER_Y"]);
        string reordered = GacPersonalBattleObservation.BuildMatchupKey(
            GacFormat.ThreeVsThree,
            isFleet: false,
            ["LEADER_A", "MEMBER_B", "MEMBER_C"],
            ["LEADER_X", "MEMBER_Y", "MEMBER_Z"]);
        string differentLeader = GacPersonalBattleObservation.BuildMatchupKey(
            GacFormat.ThreeVsThree,
            isFleet: false,
            ["MEMBER_B", "LEADER_A", "MEMBER_C"],
            ["LEADER_X", "MEMBER_Y", "MEMBER_Z"]);

        Assert.Equal(first, reordered);
        Assert.NotEqual(first, differentLeader);
        Assert.Contains("LEADER_A,MEMBER_B,MEMBER_C", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsCharacterSquadWithWrongSize()
    {
        Assert.Throws<ArgumentException>(() => GacPersonalBattleObservation.Create(
            123_456_789,
            987_654_321,
            "event-instance",
            1,
            GacFormat.ThreeVsThree,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            isFleet: false,
            ["A", "B"],
            ["X", "Y", "Z"],
            won: true,
            banners: null,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_RejectsDuplicateUnits()
    {
        Assert.Throws<ArgumentException>(() => GacPersonalBattleObservation.Create(
            123_456_789,
            987_654_321,
            "event-instance",
            1,
            GacFormat.ThreeVsThree,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            isFleet: false,
            ["A", "B", "A"],
            ["X", "Y", "Z"],
            won: false,
            banners: null,
            DateTimeOffset.UtcNow));
    }
}
