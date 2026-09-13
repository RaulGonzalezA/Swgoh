using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Domain.UnitTests.Gac;

public sealed class GacHistoricalRoundTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_BuildsDeterministicIdAndPreservesRoundData()
    {
        GacHistoricalSquad defense = GacHistoricalSquad.Create("LEADER", ["A", "B", "C", "D"], false);
        GacHistoricalRound round = GacHistoricalRound.Create(
            123_456_789,
            82,
            1,
            2,
            GacFormat.FiveVsFive,
            GacLeague.Aurodium,
            StartedAt,
            true,
            "fixture",
            [GacDefensePlacement.Create("front", defense, 1, true)],
            []);

        Assert.Equal("123456789:82:1:2:5", round.Id);
        Assert.Equal(GacLeague.Aurodium, round.League);
        Assert.True(round.FullClear);
        Assert.Single(round.Defenses);
    }

    [Fact]
    public void Squad_WithDuplicateUnits_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            GacHistoricalSquad.Create("LEADER", ["A", "leader"], false));
    }

    [Fact]
    public void Create_WithIncompleteFiveVsFiveDefense_IsRejected()
    {
        GacHistoricalSquad defense = GacHistoricalSquad.Create("LEADER", ["A", "B", "C"], false);

        Assert.Throws<ArgumentException>(() => GacHistoricalRound.Create(
            123_456_789,
            82,
            1,
            1,
            GacFormat.FiveVsFive,
            GacLeague.Kyber,
            StartedAt,
            null,
            "fixture",
            [GacDefensePlacement.Create("front", defense, 0, false)],
            []));
    }

    [Fact]
    public void Create_AllowsUndersizedOffenseSquad()
    {
        GacHistoricalSquad defender = GacHistoricalSquad.Create("DEF", ["A", "B", "C", "D"], false);
        GacHistoricalSquad attacker = GacHistoricalSquad.Create("ATT", ["X", "Y"], false);
        GacOffenseBattle battle = GacOffenseBattle.Create(
            "front",
            defender,
            attacker,
            true,
            68,
            1,
            StartedAt.AddHours(1));

        GacHistoricalRound round = GacHistoricalRound.Create(
            123_456_789,
            82,
            1,
            1,
            GacFormat.FiveVsFive,
            GacLeague.Kyber,
            StartedAt,
            true,
            "fixture",
            [],
            [battle]);

        Assert.Single(round.OffenseBattles);
        Assert.Equal(3, round.OffenseBattles.Single().Attacker.AllUnitDefinitionIds.Count);
    }

    [Fact]
    public void Create_WithAttackBeforeRoundStart_IsRejected()
    {
        GacHistoricalSquad defender = GacHistoricalSquad.Create("DEF", ["A", "B"], false);
        GacHistoricalSquad attacker = GacHistoricalSquad.Create("ATT", ["X", "Y"], false);
        GacOffenseBattle battle = GacOffenseBattle.Create(
            "front",
            defender,
            attacker,
            true,
            50,
            1,
            StartedAt.AddMinutes(-1));

        Assert.Throws<ArgumentException>(() => GacHistoricalRound.Create(
            123_456_789,
            81,
            1,
            1,
            GacFormat.ThreeVsThree,
            GacLeague.Kyber,
            StartedAt,
            null,
            "fixture",
            [],
            [battle]));
    }
}
