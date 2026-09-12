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

    [Fact]
    public void Calculate_WithCharactersAndShips_SplitsPowerAndAggregatesRosterQuality()
    {
        DateTimeOffset updatedAt = new(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);
        PlayerProfile player = PlayerProfile.Import(
            476_825_771,
            "player-id",
            "Aberronko",
            null,
            null,
            85,
            100_000,
            updatedAt,
            [
                new RosterUnit("char-1", "CHAR_1", 85, 7, 13, 7, 6, 40_000, false, 2, 1),
                new RosterUnit("char-2", "CHAR_2", 85, 7, 13, 0, 0, 20_000, false, 1, 0),
                new RosterUnit("ship-1", "SHIP_1", 85, 7, 1, 0, 0, 40_000, true, 0, 0)
            ]);

        PlayerRosterAnalysis result = PlayerRosterMetrics.Calculate(player);

        Assert.Equal(60_000, result.CharacterGalacticPower);
        Assert.Equal(40_000, result.ShipGalacticPower);
        Assert.Equal(2, result.CharacterCount);
        Assert.Equal(1, result.ShipCount);
        Assert.Equal(1, result.RelicCharacters);
        Assert.Equal(3, result.Zetas);
        Assert.Equal(1, result.Omicrons);
        Assert.Equal(1, result.FullyModdedCharacters);
        Assert.Equal(1, result.UnmoddedCharacters);
        Assert.Equal(updatedAt, result.UpdatedAtUtc);
    }
}
