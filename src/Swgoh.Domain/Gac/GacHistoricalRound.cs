namespace Swgoh.Domain.Gac;

public sealed record GacHistoricalSquad(
    string LeaderDefinitionId,
    IReadOnlyCollection<string> MemberDefinitionIds,
    bool IsFleet)
{
    public IReadOnlyCollection<string> AllUnitDefinitionIds => [LeaderDefinitionId, .. MemberDefinitionIds];

    public static GacHistoricalSquad Create(
        string leaderDefinitionId,
        IEnumerable<string> memberDefinitionIds,
        bool isFleet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderDefinitionId);
        ArgumentNullException.ThrowIfNull(memberDefinitionIds);

        string leader = leaderDefinitionId.Trim();
        string[] members =
        [
            .. memberDefinitionIds.Select(value => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Squad member definition IDs cannot be empty.", nameof(memberDefinitionIds))
                : value.Trim())
        ];

        string[] allUnits = [leader, .. members];
        if (allUnits.Distinct(StringComparer.OrdinalIgnoreCase).Count() != allUnits.Length)
        {
            throw new ArgumentException("A historical squad cannot contain duplicate units.", nameof(memberDefinitionIds));
        }

        if (isFleet && allUnits.Length > 8)
        {
            throw new ArgumentException("A historical fleet cannot contain more than eight units.", nameof(memberDefinitionIds));
        }

        return new GacHistoricalSquad(leader, members, isFleet);
    }
}

public sealed record GacDefensePlacement(
    string Zone,
    GacHistoricalSquad Squad,
    int Holds,
    bool Defeated)
{
    public static GacDefensePlacement Create(
        string zone,
        GacHistoricalSquad squad,
        int holds,
        bool defeated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zone);
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentOutOfRangeException.ThrowIfNegative(holds);
        return new GacDefensePlacement(zone.Trim(), squad, holds, defeated);
    }
}

public sealed record GacOffenseBattle(
    string Zone,
    GacHistoricalSquad Defender,
    GacHistoricalSquad Attacker,
    bool Won,
    int Banners,
    int Attempt,
    DateTimeOffset? AttackedAtUtc)
{
    public static GacOffenseBattle Create(
        string zone,
        GacHistoricalSquad defender,
        GacHistoricalSquad attacker,
        bool won,
        int banners,
        int attempt,
        DateTimeOffset? attackedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zone);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentOutOfRangeException.ThrowIfNegative(banners);
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt must be at least one.");
        }

        if (defender.IsFleet != attacker.IsFleet)
        {
            throw new ArgumentException("Attacker and defender must both be squads or both be fleets.", nameof(attacker));
        }

        return new GacOffenseBattle(zone.Trim(), defender, attacker, won, banners, attempt, attackedAtUtc);
    }
}

public sealed class GacHistoricalRound
{
    private GacHistoricalRound(
        string id,
        long allyCode,
        int season,
        int eventNumber,
        int roundNumber,
        GacFormat format,
        GacLeague league,
        DateTimeOffset startedAtUtc,
        bool? fullClear,
        string source,
        IReadOnlyCollection<GacDefensePlacement> defenses,
        IReadOnlyCollection<GacOffenseBattle> offenseBattles)
    {
        Id = id;
        AllyCode = allyCode;
        Season = season;
        EventNumber = eventNumber;
        RoundNumber = roundNumber;
        Format = format;
        League = league;
        StartedAtUtc = startedAtUtc;
        FullClear = fullClear;
        Source = source;
        Defenses = defenses;
        OffenseBattles = offenseBattles;
    }

    public string Id { get; }
    public long AllyCode { get; }
    public int Season { get; }
    public int EventNumber { get; }
    public int RoundNumber { get; }
    public GacFormat Format { get; }
    public GacLeague League { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public bool? FullClear { get; }
    public string Source { get; }
    public IReadOnlyCollection<GacDefensePlacement> Defenses { get; }
    public IReadOnlyCollection<GacOffenseBattle> OffenseBattles { get; }

    public static GacHistoricalRound Create(
        long allyCode,
        int season,
        int eventNumber,
        int roundNumber,
        GacFormat format,
        GacLeague league,
        DateTimeOffset startedAtUtc,
        bool? fullClear,
        string source,
        IEnumerable<GacDefensePlacement> defenses,
        IEnumerable<GacOffenseBattle> offenseBattles)
    {
        ValidateAllyCode(allyCode);
        if (season < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(season), season, "Season must be positive.");
        }

        if (eventNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(eventNumber), eventNumber, "Event number must be positive.");
        }

        if (roundNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roundNumber), roundNumber, "Round number must be positive.");
        }

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.");
        }

        if (!Enum.IsDefined(league))
        {
            throw new ArgumentOutOfRangeException(nameof(league), league, "Unsupported GAC league.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        string normalizedSource = source.Trim();
        if (normalizedSource.Length > 64)
        {
            throw new ArgumentException("History source cannot exceed 64 characters.", nameof(source));
        }

        ArgumentNullException.ThrowIfNull(defenses);
        ArgumentNullException.ThrowIfNull(offenseBattles);
        GacDefensePlacement[] defenseArray = [.. defenses];
        GacOffenseBattle[] offenseArray = [.. offenseBattles];

        foreach (GacDefensePlacement placement in defenseArray)
        {
            ValidateSquadSize(placement.Squad, format, requireFullCharacterSquad: true);
        }

        foreach (GacOffenseBattle battle in offenseArray)
        {
            ValidateSquadSize(battle.Defender, format, requireFullCharacterSquad: false);
            ValidateSquadSize(battle.Attacker, format, requireFullCharacterSquad: false);
            if (battle.AttackedAtUtc is DateTimeOffset attackedAtUtc && attackedAtUtc < startedAtUtc)
            {
                throw new ArgumentException("Attack timestamp cannot be before the round start.", nameof(offenseBattles));
            }
        }

        string id = $"{allyCode}:{season}:{eventNumber}:{roundNumber}:{(int)format}";
        return new GacHistoricalRound(
            id,
            allyCode,
            season,
            eventNumber,
            roundNumber,
            format,
            league,
            startedAtUtc,
            fullClear,
            normalizedSource,
            defenseArray,
            offenseArray);
    }

    private static void ValidateSquadSize(
        GacHistoricalSquad squad,
        GacFormat format,
        bool requireFullCharacterSquad)
    {
        int unitCount = squad.AllUnitDefinitionIds.Count;
        if (squad.IsFleet)
        {
            if (unitCount < 2)
            {
                throw new ArgumentException("A historical fleet requires at least a capital ship and one ship.", nameof(squad));
            }

            return;
        }

        int maxUnits = (int)format;
        if (requireFullCharacterSquad && unitCount != maxUnits)
        {
            throw new ArgumentException($"A defensive {FormatName(format)} squad requires exactly {maxUnits} units.", nameof(squad));
        }

        if (!requireFullCharacterSquad && (unitCount < 1 || unitCount > maxUnits))
        {
            throw new ArgumentException($"A {FormatName(format)} battle squad must contain between one and {maxUnits} units.", nameof(squad));
        }
    }

    private static string FormatName(GacFormat format) => format switch
    {
        GacFormat.ThreeVsThree => "3v3",
        GacFormat.FiveVsFive => "5v5",
        _ => format.ToString()
    };

    private static void ValidateAllyCode(long allyCode)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }
    }
}
