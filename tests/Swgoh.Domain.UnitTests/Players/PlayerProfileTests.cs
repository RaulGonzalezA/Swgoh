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
}
