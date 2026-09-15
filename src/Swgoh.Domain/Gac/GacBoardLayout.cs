namespace Swgoh.Domain.Gac;

public sealed record GacBoardTerritory(
    int Position,
    string Zone,
    bool IsFleet,
    int DefenseSlots);

public sealed record GacBoardDefenseSlot(
    int Position,
    int TerritoryPosition,
    int SlotInTerritory,
    string Zone,
    bool IsFleet);

public sealed record GacBoardLayout(
    GacLeague League,
    GacFormat Format,
    IReadOnlyCollection<GacBoardTerritory> Territories,
    IReadOnlyCollection<GacBoardDefenseSlot> Slots)
{
    public int SquadDefenseCount => Slots.Count(slot => !slot.IsFleet);
    public int FleetDefenseCount => Slots.Count(slot => slot.IsFleet);
    public int TotalDefenseSlots => Slots.Count;
}

public static class GacBoardLayouts
{
    private const string NorthFront = "Norte frontal";
    private const string SouthFront = "Sur frontal";
    private const string Fleet = "Flota";
    private const string SouthBack = "Sur trasera";

    public static GacBoardLayout Get(GacLeague league, GacFormat format)
    {
        if (!Enum.IsDefined(league))
        {
            throw new ArgumentOutOfRangeException(nameof(league), league, "Unsupported GAC league.");
        }

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.");
        }

        (int northFront, int southFront, int fleet, int southBack) = (league, format) switch
        {
            (GacLeague.Carbonite, _) => (1, 1, 1, 1),
            (GacLeague.Bronzium, GacFormat.FiveVsFive) => (1, 2, 1, 2),
            (GacLeague.Bronzium, GacFormat.ThreeVsThree) => (2, 2, 1, 3),
            (GacLeague.Chromium, GacFormat.FiveVsFive) => (2, 3, 2, 2),
            (GacLeague.Chromium, GacFormat.ThreeVsThree) => (3, 3, 2, 4),
            (GacLeague.Aurodium, GacFormat.FiveVsFive) => (2, 3, 2, 4),
            (GacLeague.Aurodium, GacFormat.ThreeVsThree) => (4, 4, 2, 5),
            (GacLeague.Kyber, GacFormat.FiveVsFive) => (3, 4, 3, 4),
            (GacLeague.Kyber, GacFormat.ThreeVsThree) => (5, 5, 3, 5),
            _ => throw new ArgumentOutOfRangeException(
                nameof(league),
                league,
                $"Unsupported GAC league/format combination: {league}/{format}.")
        };

        GacBoardTerritory[] territories =
        [
            new(1, NorthFront, false, northFront),
            new(2, SouthFront, false, southFront),
            new(3, Fleet, true, fleet),
            new(4, SouthBack, false, southBack)
        ];
        GacBoardDefenseSlot[] slots = BuildSlots(territories);

        GacDefenseRequirements requirements = GacDefenseRules.GetRequirements(league, format);
        if (slots.Count(slot => !slot.IsFleet) != requirements.SquadDefenseCount ||
            slots.Count(slot => slot.IsFleet) != requirements.FleetDefenseCount)
        {
            throw new InvalidOperationException(
                $"The GAC board layout for {league}/{format} is inconsistent with the defense requirements.");
        }

        return new GacBoardLayout(league, format, territories, slots);
    }

    private static GacBoardDefenseSlot[] BuildSlots(IReadOnlyCollection<GacBoardTerritory> territories)
    {
        var slots = new List<GacBoardDefenseSlot>();
        int position = 1;
        foreach (GacBoardTerritory territory in territories.OrderBy(item => item.Position))
        {
            for (int slot = 1; slot <= territory.DefenseSlots; slot++)
            {
                slots.Add(new GacBoardDefenseSlot(
                    position++,
                    territory.Position,
                    slot,
                    territory.Zone,
                    territory.IsFleet));
            }
        }

        return [.. slots];
    }
}
