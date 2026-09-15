using Swgoh.Domain.Gac;

using Xunit;

namespace Swgoh.Domain.UnitTests.Gac;

public sealed class GacDefenseRulesTests
{
    [Theory]
    [InlineData(GacLeague.Carbonite, GacFormat.FiveVsFive, 3, 1)]
    [InlineData(GacLeague.Carbonite, GacFormat.ThreeVsThree, 3, 1)]
    [InlineData(GacLeague.Bronzium, GacFormat.FiveVsFive, 5, 1)]
    [InlineData(GacLeague.Bronzium, GacFormat.ThreeVsThree, 7, 1)]
    [InlineData(GacLeague.Chromium, GacFormat.FiveVsFive, 7, 2)]
    [InlineData(GacLeague.Chromium, GacFormat.ThreeVsThree, 10, 2)]
    [InlineData(GacLeague.Aurodium, GacFormat.FiveVsFive, 9, 2)]
    [InlineData(GacLeague.Aurodium, GacFormat.ThreeVsThree, 13, 2)]
    [InlineData(GacLeague.Kyber, GacFormat.FiveVsFive, 11, 3)]
    [InlineData(GacLeague.Kyber, GacFormat.ThreeVsThree, 15, 3)]
    public void GetRequirements_ReturnsLeagueAndFormatSpecificCounts(
        GacLeague league,
        GacFormat format,
        int expectedSquads,
        int expectedFleets)
    {
        GacDefenseRequirements requirements = GacDefenseRules.GetRequirements(league, format);

        Assert.Equal(league, requirements.League);
        Assert.Equal(format, requirements.Format);
        Assert.Equal(expectedSquads, requirements.SquadDefenseCount);
        Assert.Equal(expectedFleets, requirements.FleetDefenseCount);
    }

    [Theory]
    [InlineData(GacLeague.Carbonite, GacFormat.FiveVsFive, 1, 1, 1, 1)]
    [InlineData(GacLeague.Bronzium, GacFormat.FiveVsFive, 1, 2, 1, 2)]
    [InlineData(GacLeague.Chromium, GacFormat.ThreeVsThree, 3, 3, 2, 4)]
    [InlineData(GacLeague.Aurodium, GacFormat.FiveVsFive, 2, 3, 2, 4)]
    [InlineData(GacLeague.Kyber, GacFormat.FiveVsFive, 3, 4, 3, 4)]
    [InlineData(GacLeague.Kyber, GacFormat.ThreeVsThree, 5, 5, 3, 5)]
    public void BoardLayout_ReturnsTerritoryCapacitiesAndMatchesRequirements(
        GacLeague league,
        GacFormat format,
        int northFront,
        int southFront,
        int fleets,
        int southBack)
    {
        GacBoardLayout layout = GacBoardLayouts.Get(league, format);
        GacDefenseRequirements requirements = GacDefenseRules.GetRequirements(league, format);

        Assert.Collection(
            layout.Territories.OrderBy(item => item.Position),
            territory => Assert.Equal(northFront, territory.DefenseSlots),
            territory => Assert.Equal(southFront, territory.DefenseSlots),
            territory => Assert.Equal(fleets, territory.DefenseSlots),
            territory => Assert.Equal(southBack, territory.DefenseSlots));
        Assert.Equal(requirements.SquadDefenseCount, layout.SquadDefenseCount);
        Assert.Equal(requirements.FleetDefenseCount, layout.FleetDefenseCount);
        Assert.Equal(requirements.SquadDefenseCount + requirements.FleetDefenseCount, layout.TotalDefenseSlots);
        Assert.Equal(layout.TotalDefenseSlots, layout.Slots.Select(slot => slot.Position).Distinct().Count());
    }

    [Theory]
    [InlineData(GacFormat.FiveVsFive, 9, 11)]
    [InlineData(GacFormat.ThreeVsThree, 13, 15)]
    public void Compare_AurodiumToKyber_ReportsAdditionalDefenseSlots(
        GacFormat format,
        int expectedFromSquads,
        int expectedToSquads)
    {
        GacLeagueTransition transition = GacDefenseRules.Compare(
            GacLeague.Aurodium,
            GacLeague.Kyber,
            format);

        Assert.Equal(expectedFromSquads, transition.From.SquadDefenseCount);
        Assert.Equal(expectedToSquads, transition.To.SquadDefenseCount);
        Assert.Equal(2, transition.SquadDefenseDelta);
        Assert.Equal(1, transition.FleetDefenseDelta);
        Assert.Equal(2, transition.AdditionalSquadDefenses);
        Assert.Equal(1, transition.AdditionalFleetDefenses);
        Assert.True(transition.IsPromotion);
        Assert.False(transition.IsDemotion);
    }

    [Fact]
    public void Compare_KyberToAurodium_DoesNotReportAdditionalDefenseSlots()
    {
        GacLeagueTransition transition = GacDefenseRules.Compare(
            GacLeague.Kyber,
            GacLeague.Aurodium,
            GacFormat.FiveVsFive);

        Assert.Equal(-2, transition.SquadDefenseDelta);
        Assert.Equal(-1, transition.FleetDefenseDelta);
        Assert.Equal(0, transition.AdditionalSquadDefenses);
        Assert.Equal(0, transition.AdditionalFleetDefenses);
        Assert.False(transition.IsPromotion);
        Assert.True(transition.IsDemotion);
    }

    [Fact]
    public void GetRequirements_WithUnknownLeague_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GacDefenseRules.GetRequirements((GacLeague)99, GacFormat.FiveVsFive));
    }
}
