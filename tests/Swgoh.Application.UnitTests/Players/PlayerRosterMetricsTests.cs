using Swgoh.Application.Players;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Players;

public sealed class PlayerRosterMetricsTests
{
    [Fact]
    public void Calculate_WithR10Character_CountsR10AndAllLowerThresholds()
    {
        RosterUnit[] roster =
        [
            new RosterUnit("unit-1", "TEST_CHARACTER", 85, 7, 13, 10, 6, 40_000)
        ];

        PlayerProfile player = PlayerProfile.Import(
            476_825_771,
            "player-id",
            "Aberronko",
            null,
            null,
            85,
            40_000,
            DateTimeOffset.UtcNow,
            roster);

        PlayerRosterAnalysis result = PlayerRosterMetrics.Calculate(player);

        Assert.Equal(1, result.RelicCharacters);
        Assert.Equal(1, result.Relic7Plus);
        Assert.Equal(1, result.Relic8Plus);
        Assert.Equal(1, result.Relic9Plus);
        Assert.Equal(1, result.Relic10);
    }
}
