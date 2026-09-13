using Swgoh.Domain.Conquest;

using Xunit;

namespace Swgoh.Domain.UnitTests.Conquest;

public sealed class ConquestPlanTests
{
    [Fact]
    public void Create_FactionFeatWithFullTeam_IsValid()
    {
        ConquestFeatRule rule = ConquestFeatRule.Create(
            ConquestFeatRuleType.Faction,
            "JEDI",
            [],
            minimumMatchingUnits: 5);

        ConquestFeat feat = ConquestFeat.Create(
            Guid.NewGuid(),
            "Gana con Jedi",
            ConquestFeatScope.Sector,
            sector: 2,
            points: 10,
            target: 5,
            progress: 2,
            expectedProgressPerBattle: 1,
            rule);

        Assert.Equal(3, feat.Remaining);
        Assert.False(feat.IsComplete);
        Assert.Equal(5, feat.Rule.MinimumMatchingUnits);
    }

    [Fact]
    public void Create_GlobalFeatRejectsSector()
    {
        ConquestFeatRule rule = ConquestFeatRule.Create(
            ConquestFeatRuleType.AnyCharacter,
            null,
            [],
            1);

        Assert.Throws<ArgumentException>(() => ConquestFeat.Create(
            Guid.NewGuid(),
            "Global",
            ConquestFeatScope.Global,
            sector: 1,
            points: 5,
            target: 10,
            progress: 0,
            expectedProgressPerBattle: 1,
            rule));
    }

    [Fact]
    public void Create_SpecificUnitsRejectsImpossibleMinimum()
    {
        Assert.Throws<ArgumentException>(() => ConquestFeatRule.Create(
            ConquestFeatRuleType.SpecificUnits,
            null,
            ["A", "B"],
            minimumMatchingUnits: 3));
    }
}
