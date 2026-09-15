namespace Swgoh.Domain.Players;

public sealed class PlayerProfile
{
    private readonly List<RosterUnit> _roster;
    private readonly List<PlayerDatacron> _datacrons;

    private PlayerProfile(
        long allyCode,
        string playerId,
        string name,
        string? guildId,
        string? guildName,
        int level,
        long galacticPower,
        DateTimeOffset updatedAtUtc,
        IEnumerable<RosterUnit> roster,
        IEnumerable<PlayerDatacron>? datacrons)
    {
        AllyCode = allyCode;
        PlayerId = playerId;
        Name = name;
        GuildId = guildId;
        GuildName = guildName;
        Level = level;
        GalacticPower = galacticPower;
        UpdatedAtUtc = updatedAtUtc;
        _roster = [.. roster];
        _datacrons = datacrons is null ? [] : [.. datacrons];
    }

    public long AllyCode { get; private set; }
    public string PlayerId { get; private set; }
    public string Name { get; private set; }
    public string? GuildId { get; private set; }
    public string? GuildName { get; private set; }
    public int Level { get; private set; }
    public long GalacticPower { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyList<RosterUnit> Roster => _roster;
    public IReadOnlyList<PlayerDatacron> Datacrons => _datacrons;

    public static PlayerProfile Create(long allyCode, string name, long galacticPower, DateTimeOffset updatedAtUtc)
    {
        Validate(allyCode, name, galacticPower);
        return new PlayerProfile(allyCode, string.Empty, name.Trim(), null, null, 0, galacticPower, updatedAtUtc, [], []);
    }

    public static PlayerProfile Import(
        long allyCode,
        string playerId,
        string name,
        string? guildId,
        string? guildName,
        int level,
        long galacticPower,
        DateTimeOffset updatedAtUtc,
        IEnumerable<RosterUnit> roster,
        IEnumerable<PlayerDatacron>? datacrons = null)
    {
        Validate(allyCode, name, galacticPower);
        ArgumentNullException.ThrowIfNull(roster);

        return new PlayerProfile(
            allyCode,
            playerId?.Trim() ?? string.Empty,
            name.Trim(),
            string.IsNullOrWhiteSpace(guildId) ? null : guildId.Trim(),
            string.IsNullOrWhiteSpace(guildName) ? null : guildName.Trim(),
            level,
            galacticPower,
            updatedAtUtc,
            roster,
            datacrons);
    }

    public void Refresh(string name, long galacticPower, DateTimeOffset updatedAtUtc)
    {
        Validate(AllyCode, name, galacticPower);
        Name = name.Trim();
        GalacticPower = galacticPower;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static void Validate(long allyCode, string name, long galacticPower)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(allyCode, 100_000_000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(allyCode, 999_999_999);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(galacticPower);
    }
}
