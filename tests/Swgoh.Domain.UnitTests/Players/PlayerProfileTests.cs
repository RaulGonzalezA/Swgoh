using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Domain.UnitTests.Players;

public sealed class PlayerProfileTests
{
    [Fact]
    public void Create_WithValidData_CreatesPlayer()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PlayerProfile player = PlayerProfile.Create(476_825_771, "Aberronko", 10_000_000, now);

        Assert.Equal(476_825_771, player.AllyCode);
        Assert.Equal("Aberronko", player.Name);
        Assert.Equal(10_000_000, player.GalacticPower);
        Assert.Equal(now, player.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(99_999_999)]
    [InlineData(1_000_000_000)]
    public void Create_WithInvalidAllyCode_Throws(long allyCode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerProfile.Create(allyCode, "Player", 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_WithBlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PlayerProfile.Create(476_825_771, "   ", 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_WithNegativeGalacticPower_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerProfile.Create(476_825_771, "Player", -1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Import_NormalizesOptionalTextAndCopiesRoster()
    {
        RosterUnit[] roster =
        [
            new RosterUnit("unit-1", "CHARACTER", 85, 7, 13, 7, 6)
        ];

        PlayerProfile player = PlayerProfile.Import(
            476_825_771,
            "  player-id  ",
            "  Aberronko  ",
            "  guild-id  ",
            "  Guild  ",
            85,
            1,
            DateTimeOffset.UtcNow,
            roster);
        roster[0] = new RosterUnit("changed", "CHANGED", 1, 1, 1, 0, 0);

        Assert.Equal("player-id", player.PlayerId);
        Assert.Equal("Aberronko", player.Name);
        Assert.Equal("guild-id", player.GuildId);
        Assert.Equal("Guild", player.GuildName);
        Assert.Equal("unit-1", Assert.Single(player.Roster).Id);
    }
}
