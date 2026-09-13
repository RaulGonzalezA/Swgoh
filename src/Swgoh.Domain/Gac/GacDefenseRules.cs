namespace Swgoh.Domain.Gac;

public static class GacDefenseRules
{
    public static GacDefenseRequirements GetRequirements(GacLeague league, GacFormat format) =>
        (league, format) switch
        {
            (GacLeague.Carbonite, GacFormat.FiveVsFive) => new(league, format, 3, 1),
            (GacLeague.Carbonite, GacFormat.ThreeVsThree) => new(league, format, 3, 1),
            (GacLeague.Bronzium, GacFormat.FiveVsFive) => new(league, format, 5, 1),
            (GacLeague.Bronzium, GacFormat.ThreeVsThree) => new(league, format, 7, 1),
            (GacLeague.Chromium, GacFormat.FiveVsFive) => new(league, format, 7, 2),
            (GacLeague.Chromium, GacFormat.ThreeVsThree) => new(league, format, 10, 2),
            (GacLeague.Aurodium, GacFormat.FiveVsFive) => new(league, format, 9, 2),
            (GacLeague.Aurodium, GacFormat.ThreeVsThree) => new(league, format, 13, 2),
            (GacLeague.Kyber, GacFormat.FiveVsFive) => new(league, format, 11, 3),
            (GacLeague.Kyber, GacFormat.ThreeVsThree) => new(league, format, 15, 3),
            _ => throw new ArgumentOutOfRangeException(
                nameof(league),
                league,
                $"Unsupported GAC league/format combination: {league}/{format}.")
        };

    public static GacLeagueTransition Compare(
        GacLeague fromLeague,
        GacLeague toLeague,
        GacFormat format) =>
        new(GetRequirements(fromLeague, format), GetRequirements(toLeague, format));
}
