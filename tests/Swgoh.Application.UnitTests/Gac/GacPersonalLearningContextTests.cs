using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Xunit;

namespace Swgoh.Application.UnitTests.Gac;

public sealed class GacPersonalLearningContextTests
{
    [Theory]
    [InlineData(1, 1, 0.1, 0.3)]
    [InlineData(8, 8, 3.9, 4.1)]
    [InlineData(8, 0, -4.1, -3.9)]
    public void Evaluate_ExactHistory_IsBayesianAndBounded(int uses, int wins, decimal min, decimal max)
    {
        GacTeamPresetDetails preset = Preset(["A", "B", "C"]);
        GacVisibleDefenseDetails defense = Defense(["X", "Y", "Z"]);
        var stats = Statistics(preset, defense, uses, wins);

        GacPersonalLearningSignal signal = GacPersonalLearningContext.From([stats]).Evaluate(defense, preset);

        Assert.Equal("Exact", signal.Scope);
        Assert.Equal(uses, signal.Samples);
        Assert.InRange(signal.Adjustment, min, max);
    }

    [Fact]
    public void Evaluate_LeaderPairFallback_RequiresThreeSamplesAndStaysWeaker()
    {
        GacTeamPresetDetails target = Preset(["A", "B", "C"]);
        GacVisibleDefenseDetails defense = Defense(["X", "Y", "Z"]);
        GacPersonalMatchupStatistics one = Statistics(Preset(["A", "D", "E"]), Defense(["X", "Q", "R"]), 1, 1);
        GacPersonalMatchupStatistics two = Statistics(Preset(["A", "F", "G"]), Defense(["X", "S", "T"]), 1, 1);
        GacPersonalMatchupStatistics three = Statistics(Preset(["A", "H", "I"]), Defense(["X", "U", "V"]), 1, 1);

        GacPersonalLearningSignal insufficient = GacPersonalLearningContext.From([one, two]).Evaluate(defense, target);
        GacPersonalLearningSignal enough = GacPersonalLearningContext.From([one, two, three]).Evaluate(defense, target);

        Assert.Equal("None", insufficient.Scope);
        Assert.Equal("LeaderPair", enough.Scope);
        Assert.InRange(enough.Adjustment, 0.1m, 0.5m);
    }

    private static GacPersonalMatchupStatistics Statistics(
        GacTeamPresetDetails preset,
        GacVisibleDefenseDetails defense,
        int uses,
        int wins)
    {
        string[] attackers = [.. preset.Squad.AllUnits.Select(unit => unit.DefinitionId)];
        string[] defenders = [.. defense.Squad.AllUnits.Select(unit => unit.DefinitionId)];
        return new(
            GacPersonalBattleObservation.BuildMatchupKey(preset.Format, false, attackers, defenders),
            false,
            attackers,
            defenders,
            uses,
            wins,
            wins / (decimal)uses,
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    private static GacTeamPresetDetails Preset(string[] ids)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(Unit)];
        return new(
            Guid.NewGuid(),
            123_456_789,
            string.Join('-', ids),
            GacFormat.ThreeVsThree,
            GacPlannerTeamUse.Offense,
            new GacPlannerSquadDetails(units[0], [.. units.Skip(1)], false),
            DateTimeOffset.UtcNow);
    }

    private static GacVisibleDefenseDetails Defense(string[] ids)
    {
        GacPlannerUnitDetails[] units = [.. ids.Select(Unit)];
        return new(Guid.NewGuid(), "Sur frontal", null, new(units[0], [.. units.Skip(1)], false), false);
    }

    private static GacPlannerUnitDetails Unit(string id) => new(id, id, null, false, 20_000, 7, 1, 0);
}
