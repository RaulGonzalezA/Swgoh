namespace Swgoh.Domain.Squads;

public sealed class SquadVariant
{
    private readonly List<string> memberDefinitionIds;

    private SquadVariant(
        SquadFormat format,
        string key,
        string name,
        string leaderDefinitionId,
        IEnumerable<string> memberDefinitionIds)
    {
        Format = format;
        Key = key;
        Name = name;
        LeaderDefinitionId = leaderDefinitionId;
        this.memberDefinitionIds = [.. memberDefinitionIds];
    }

    public SquadFormat Format { get; }
    public string Key { get; }
    public string Name { get; }
    public string LeaderDefinitionId { get; }
    public IReadOnlyList<string> MemberDefinitionIds => memberDefinitionIds;
    public IReadOnlyList<string> AllUnitDefinitionIds => [LeaderDefinitionId, .. memberDefinitionIds];

    public static SquadVariant Create(
        SquadFormat format,
        string key,
        string name,
        string leaderDefinitionId,
        IEnumerable<string> memberDefinitionIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderDefinitionId);
        ArgumentNullException.ThrowIfNull(memberDefinitionIds);

        string leader = leaderDefinitionId.Trim();
        string[] members = [.. memberDefinitionIds
            .Select(value => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Squad member definition IDs cannot be empty.", nameof(memberDefinitionIds))
                : value.Trim())];

        int requiredMemberCount = GetRequiredUnitCount(format) - 1;
        if (members.Length != requiredMemberCount)
        {
            throw new ArgumentException(
                $"A {FormatDisplayName(format)} squad variant requires exactly {requiredMemberCount} members in addition to the leader.",
                nameof(memberDefinitionIds));
        }

        string[] allUnits = [leader, .. members];
        if (allUnits.Distinct(StringComparer.OrdinalIgnoreCase).Count() != allUnits.Length)
        {
            throw new ArgumentException("A squad variant cannot contain duplicate units.", nameof(memberDefinitionIds));
        }

        return new SquadVariant(format, key.Trim(), name.Trim(), leader, members);
    }

    private static int GetRequiredUnitCount(SquadFormat format) => format switch
    {
        SquadFormat.ThreeVsThree => 3,
        SquadFormat.FiveVsFive => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported squad format.")
    };

    private static string FormatDisplayName(SquadFormat format) => format switch
    {
        SquadFormat.ThreeVsThree => "3v3",
        SquadFormat.FiveVsFive => "5v5",
        _ => format.ToString()
    };
}
