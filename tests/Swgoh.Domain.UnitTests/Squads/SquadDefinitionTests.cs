using Swgoh.Domain.Squads;

using Xunit;

namespace Swgoh.Domain.UnitTests.Squads;

public sealed class SquadDefinitionTests
{
    [Fact]
    public void Create_ThreeVsThreeVariant_RequiresLeaderAndTwoUniqueMembers()
    {
        SquadVariant variant = SquadVariant.Create(
            SquadFormat.ThreeVsThree,
            "default",
            "Default",
            "LEADER",
            ["MEMBER1", "MEMBER2"]);

        Assert.Equal(SquadFormat.ThreeVsThree, variant.Format);
        Assert.Equal("LEADER", variant.LeaderDefinitionId);
        Assert.Equal(["MEMBER1", "MEMBER2"], variant.MemberDefinitionIds);
        Assert.Equal(["LEADER", "MEMBER1", "MEMBER2"], variant.AllUnitDefinitionIds);
    }

    [Fact]
    public void Create_FiveVsFiveVariant_WithWrongMemberCount_Throws()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => SquadVariant.Create(
            SquadFormat.FiveVsFive,
            "default",
            "Default",
            "LEADER",
            ["MEMBER1", "MEMBER2"]));

        Assert.Contains("exactly 4 members", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_VariantWithDuplicateUnit_Throws()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => SquadVariant.Create(
            SquadFormat.ThreeVsThree,
            "default",
            "Default",
            "LEADER",
            ["MEMBER1", "leader"]));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_NormalizesTagsAndRequiresUniqueVariantKeys()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-09-13T08:00:00Z");
        SquadVariant primary = SquadVariant.Create(
            SquadFormat.ThreeVsThree,
            "default",
            "Default",
            "LEADER",
            ["MEMBER1", "MEMBER2"]);
        SquadDefinition definition = SquadDefinition.Create(
            Guid.NewGuid(),
            "  Test squad  ",
            SquadFormat.ThreeVsThree,
            SquadUse.Defense,
            [" GAC ", "sith", "gac"],
            [primary],
            now);

        Assert.Equal("Test squad", definition.Name);
        Assert.Equal(["gac", "sith"], definition.Tags);
        Assert.Equal(now, definition.CreatedAtUtc);
        Assert.Equal(now, definition.UpdatedAtUtc);

        SquadVariant duplicateKey = SquadVariant.Create(
            SquadFormat.ThreeVsThree,
            "DEFAULT",
            "Alternative",
            "LEADER2",
            ["MEMBER3", "MEMBER4"]);

        Assert.Throws<ArgumentException>(() => SquadDefinition.Create(
            Guid.NewGuid(),
            "Invalid",
            SquadFormat.ThreeVsThree,
            SquadUse.Flexible,
            [],
            [primary, duplicateKey],
            now));
    }

    [Fact]
    public void Update_CanChangeFormatOnlyWithMatchingVariants()
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-09-13T08:00:00Z");
        SquadVariant threeVsThree = SquadVariant.Create(
            SquadFormat.ThreeVsThree,
            "default",
            "Default",
            "LEADER",
            ["MEMBER1", "MEMBER2"]);
        SquadDefinition definition = SquadDefinition.Create(
            Guid.NewGuid(),
            "Squad",
            SquadFormat.ThreeVsThree,
            SquadUse.Flexible,
            [],
            [threeVsThree],
            createdAt);

        SquadVariant fiveVsFive = SquadVariant.Create(
            SquadFormat.FiveVsFive,
            "default",
            "Default",
            "LEADER",
            ["MEMBER1", "MEMBER2", "MEMBER3", "MEMBER4"]);
        DateTimeOffset updatedAt = createdAt.AddMinutes(5);
        definition.Update(
            "Updated squad",
            SquadFormat.FiveVsFive,
            SquadUse.Offense,
            ["gac"],
            [fiveVsFive],
            updatedAt);

        Assert.Equal(SquadFormat.FiveVsFive, definition.Format);
        Assert.Equal(SquadUse.Offense, definition.Use);
        Assert.Equal(updatedAt, definition.UpdatedAtUtc);
        Assert.Single(definition.Variants);
    }
}
