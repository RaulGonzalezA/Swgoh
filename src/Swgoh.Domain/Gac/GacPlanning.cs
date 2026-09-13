namespace Swgoh.Domain.Gac;

public enum GacPlannerTeamUse
{
    Flexible = 0,
    Offense = 1,
    Defense = 2
}

public enum GacAttackPlanStatus
{
    Planned = 1,
    Won = 2,
    Failed = 3,
    Cancelled = 4
}

public sealed record GacPlannerSquad(
    string LeaderDefinitionId,
    IReadOnlyCollection<string> MemberDefinitionIds,
    bool IsFleet)
{
    public IReadOnlyCollection<string> AllUnitDefinitionIds => [LeaderDefinitionId, .. MemberDefinitionIds];

    public static GacPlannerSquad Create(
        GacFormat format,
        string leaderDefinitionId,
        IEnumerable<string> memberDefinitionIds,
        bool isFleet)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.");
        }

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
            throw new ArgumentException("A planned squad cannot contain duplicate units.", nameof(memberDefinitionIds));
        }

        if (isFleet)
        {
            if (allUnits.Length is < 2 or > 8)
            {
                throw new ArgumentException(
                    "A planned fleet must contain a capital ship and between one and seven ships.",
                    nameof(memberDefinitionIds));
            }
        }
        else if (allUnits.Length != (int)format)
        {
            throw new ArgumentException(
                $"A planned {FormatName(format)} squad requires exactly {(int)format} units.",
                nameof(memberDefinitionIds));
        }

        return new GacPlannerSquad(leader, members, isFleet);
    }

    private static string FormatName(GacFormat format) => format switch
    {
        GacFormat.ThreeVsThree => "3v3",
        GacFormat.FiveVsFive => "5v5",
        _ => format.ToString()
    };
}

public sealed class GacTeamPreset
{
    private GacTeamPreset(
        Guid id,
        long allyCode,
        string name,
        GacFormat format,
        GacPlannerTeamUse use,
        GacPlannerSquad squad,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        AllyCode = allyCode;
        Name = name;
        Format = format;
        Use = use;
        Squad = squad;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }
    public long AllyCode { get; }
    public string Name { get; private set; }
    public GacFormat Format { get; private set; }
    public GacPlannerTeamUse Use { get; private set; }
    public GacPlannerSquad Squad { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static GacTeamPreset Create(
        Guid id,
        long allyCode,
        string name,
        GacFormat format,
        GacPlannerTeamUse use,
        GacPlannerSquad squad,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Team preset ID cannot be empty.", nameof(id));
        }

        ValidateAllyCode(allyCode);
        string normalizedName = ValidateName(name);
        ValidateEnums(format, use);
        ArgumentNullException.ThrowIfNull(squad);
        ValidateSquadFormat(format, squad);

        return new GacTeamPreset(
            id,
            allyCode,
            normalizedName,
            format,
            use,
            squad,
            createdAtUtc,
            createdAtUtc);
    }

    public static GacTeamPreset Restore(
        Guid id,
        long allyCode,
        string name,
        GacFormat format,
        GacPlannerTeamUse use,
        GacPlannerSquad squad,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        GacTeamPreset preset = Create(id, allyCode, name, format, use, squad, createdAtUtc);
        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc), "Updated time cannot be earlier than created time.");
        }

        preset.UpdatedAtUtc = updatedAtUtc;
        return preset;
    }

    public void Update(
        string name,
        GacFormat format,
        GacPlannerTeamUse use,
        GacPlannerSquad squad,
        DateTimeOffset updatedAtUtc)
    {
        if (updatedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc), "Updated time cannot be earlier than created time.");
        }

        string normalizedName = ValidateName(name);
        ValidateEnums(format, use);
        ArgumentNullException.ThrowIfNull(squad);
        ValidateSquadFormat(format, squad);

        Name = normalizedName;
        Format = format;
        Use = use;
        Squad = squad;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = name.Trim();
        if (normalized.Length > 80)
        {
            throw new ArgumentException("Team preset name cannot exceed 80 characters.", nameof(name));
        }

        return normalized;
    }

    private static void ValidateEnums(GacFormat format, GacPlannerTeamUse use)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.");
        }

        if (!Enum.IsDefined(use))
        {
            throw new ArgumentOutOfRangeException(nameof(use), use, "Unsupported team use.");
        }
    }

    private static void ValidateSquadFormat(GacFormat format, GacPlannerSquad squad)
    {
        if (!squad.IsFleet && squad.AllUnitDefinitionIds.Count != (int)format)
        {
            throw new ArgumentException("Team preset squad size does not match its GAC format.", nameof(squad));
        }
    }

    private static void ValidateAllyCode(long allyCode)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(allyCode), allyCode, "Ally code must contain exactly nine digits.");
        }
    }
}

public sealed record GacOwnDefenseAssignment(Guid Id, string Zone, Guid TeamPresetId)
{
    public static GacOwnDefenseAssignment Create(Guid id, string zone, Guid teamPresetId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Defense assignment ID cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(zone);
        if (teamPresetId == Guid.Empty)
        {
            throw new ArgumentException("Team preset ID cannot be empty.", nameof(teamPresetId));
        }

        return new GacOwnDefenseAssignment(id, zone.Trim(), teamPresetId);
    }
}

public sealed record GacVisibleDefense(
    Guid Id,
    string Zone,
    string? Label,
    GacPlannerSquad Squad)
{
    public static GacVisibleDefense Create(
        Guid id,
        string zone,
        string? label,
        GacPlannerSquad squad)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Visible defense ID cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(zone);
        ArgumentNullException.ThrowIfNull(squad);
        string? normalizedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (normalizedLabel?.Length > 80)
        {
            throw new ArgumentException("Visible defense label cannot exceed 80 characters.", nameof(label));
        }

        return new GacVisibleDefense(id, zone.Trim(), normalizedLabel, squad);
    }
}

public sealed record GacAttackAssignment(
    Guid Id,
    Guid DefenseId,
    Guid TeamPresetId,
    int Attempt,
    GacAttackPlanStatus Status,
    string? Notes)
{
    public static GacAttackAssignment Create(
        Guid id,
        Guid defenseId,
        Guid teamPresetId,
        int attempt,
        GacAttackPlanStatus status,
        string? notes)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Attack assignment ID cannot be empty.", nameof(id));
        }

        if (defenseId == Guid.Empty)
        {
            throw new ArgumentException("Defense ID cannot be empty.", nameof(defenseId));
        }

        if (teamPresetId == Guid.Empty)
        {
            throw new ArgumentException("Team preset ID cannot be empty.", nameof(teamPresetId));
        }

        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt must be at least one.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported attack status.");
        }

        string? normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (normalizedNotes?.Length > 500)
        {
            throw new ArgumentException("Attack notes cannot exceed 500 characters.", nameof(notes));
        }

        return new GacAttackAssignment(id, defenseId, teamPresetId, attempt, status, normalizedNotes);
    }
}

public sealed class GacRoundPlan
{
    private readonly List<GacOwnDefenseAssignment> ownDefenses;
    private readonly List<GacVisibleDefense> visibleDefenses;
    private readonly List<GacAttackAssignment> attacks;

    private GacRoundPlan(
        string id,
        long playerAllyCode,
        long opponentAllyCode,
        string eventId,
        string eventInstanceId,
        int roundNumber,
        GacFormat format,
        GacLeague league,
        IEnumerable<GacOwnDefenseAssignment> ownDefenses,
        IEnumerable<GacVisibleDefense> visibleDefenses,
        IEnumerable<GacAttackAssignment> attacks,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        PlayerAllyCode = playerAllyCode;
        OpponentAllyCode = opponentAllyCode;
        EventId = eventId;
        EventInstanceId = eventInstanceId;
        RoundNumber = roundNumber;
        Format = format;
        League = league;
        this.ownDefenses = [.. ownDefenses];
        this.visibleDefenses = [.. visibleDefenses];
        this.attacks = [.. attacks];
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string Id { get; }
    public long PlayerAllyCode { get; }
    public long OpponentAllyCode { get; }
    public string EventId { get; }
    public string EventInstanceId { get; }
    public int RoundNumber { get; }
    public GacFormat Format { get; }
    public GacLeague League { get; }
    public IReadOnlyList<GacOwnDefenseAssignment> OwnDefenses => ownDefenses;
    public IReadOnlyList<GacVisibleDefense> VisibleDefenses => visibleDefenses;
    public IReadOnlyList<GacAttackAssignment> Attacks => attacks;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static GacRoundPlan Create(
        long playerAllyCode,
        long opponentAllyCode,
        string eventId,
        string eventInstanceId,
        int roundNumber,
        GacFormat format,
        GacLeague league,
        DateTimeOffset createdAtUtc)
    {
        ValidateIdentity(
            playerAllyCode,
            opponentAllyCode,
            eventId,
            eventInstanceId,
            roundNumber,
            format,
            league);

        string id = BuildId(playerAllyCode, eventInstanceId, roundNumber);
        return new GacRoundPlan(
            id,
            playerAllyCode,
            opponentAllyCode,
            eventId.Trim(),
            eventInstanceId.Trim(),
            roundNumber,
            format,
            league,
            [],
            [],
            [],
            createdAtUtc,
            createdAtUtc);
    }

    public static GacRoundPlan Restore(
        long playerAllyCode,
        long opponentAllyCode,
        string eventId,
        string eventInstanceId,
        int roundNumber,
        GacFormat format,
        GacLeague league,
        IEnumerable<GacOwnDefenseAssignment> ownDefenses,
        IEnumerable<GacVisibleDefense> visibleDefenses,
        IEnumerable<GacAttackAssignment> attacks,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        GacRoundPlan plan = Create(
            playerAllyCode,
            opponentAllyCode,
            eventId,
            eventInstanceId,
            roundNumber,
            format,
            league,
            createdAtUtc);
        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc), "Updated time cannot be earlier than created time.");
        }

        plan.Replace(ownDefenses, visibleDefenses, attacks, updatedAtUtc);
        return plan;
    }

    public void Replace(
        IEnumerable<GacOwnDefenseAssignment> ownDefenseAssignments,
        IEnumerable<GacVisibleDefense> enemyVisibleDefenses,
        IEnumerable<GacAttackAssignment> attackAssignments,
        DateTimeOffset updatedAtUtc)
    {
        if (updatedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc), "Updated time cannot be earlier than created time.");
        }

        ArgumentNullException.ThrowIfNull(ownDefenseAssignments);
        ArgumentNullException.ThrowIfNull(enemyVisibleDefenses);
        ArgumentNullException.ThrowIfNull(attackAssignments);

        GacOwnDefenseAssignment[] own = [.. ownDefenseAssignments];
        GacVisibleDefense[] visible = [.. enemyVisibleDefenses];
        GacAttackAssignment[] plannedAttacks = [.. attackAssignments];

        EnsureUniqueIds(own.Select(item => item.Id), nameof(ownDefenseAssignments));
        EnsureUniqueIds(visible.Select(item => item.Id), nameof(enemyVisibleDefenses));
        EnsureUniqueIds(plannedAttacks.Select(item => item.Id), nameof(attackAssignments));

        HashSet<Guid> visibleIds = visible.Select(item => item.Id).ToHashSet();
        if (plannedAttacks.Any(attack => !visibleIds.Contains(attack.DefenseId)))
        {
            throw new ArgumentException("Every attack must reference a visible enemy defense.", nameof(attackAssignments));
        }

        bool duplicateAttempt = plannedAttacks
            .GroupBy(attack => attack.DefenseId)
            .Any(group => group.Select(attack => attack.Attempt).Distinct().Count() != group.Count());
        if (duplicateAttempt)
        {
            throw new ArgumentException(
                "Attack attempt numbers must be unique for each visible defense.",
                nameof(attackAssignments));
        }

        ownDefenses.Clear();
        ownDefenses.AddRange(own);
        visibleDefenses.Clear();
        visibleDefenses.AddRange(visible);
        attacks.Clear();
        attacks.AddRange(plannedAttacks);
        UpdatedAtUtc = updatedAtUtc;
    }

    public static string BuildId(long playerAllyCode, string eventInstanceId, int roundNumber)
    {
        ValidateAllyCode(playerAllyCode, nameof(playerAllyCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(eventInstanceId);
        if (roundNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roundNumber), roundNumber, "Round number must be positive.");
        }

        return $"{playerAllyCode}:{eventInstanceId.Trim()}:{roundNumber}";
    }

    private static void ValidateIdentity(
        long playerAllyCode,
        long opponentAllyCode,
        string eventId,
        string eventInstanceId,
        int roundNumber,
        GacFormat format,
        GacLeague league)
    {
        ValidateAllyCode(playerAllyCode, nameof(playerAllyCode));
        ValidateAllyCode(opponentAllyCode, nameof(opponentAllyCode));
        if (playerAllyCode == opponentAllyCode)
        {
            throw new ArgumentException("Player and opponent ally codes must be different.", nameof(opponentAllyCode));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventInstanceId);
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
    }

    private static void EnsureUniqueIds(IEnumerable<Guid> ids, string parameterName)
    {
        Guid[] values = [.. ids];
        if (values.Any(id => id == Guid.Empty) || values.Distinct().Count() != values.Length)
        {
            throw new ArgumentException("Planning item IDs must be non-empty and unique.", parameterName);
        }
    }

    private static void ValidateAllyCode(long allyCode, string parameterName)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(parameterName, allyCode, "Ally code must contain exactly nine digits.");
        }
    }
}
