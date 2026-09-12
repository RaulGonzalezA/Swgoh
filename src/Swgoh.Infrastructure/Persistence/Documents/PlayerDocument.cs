namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class PlayerDocument
{
    public long Id { get; set; }
    public long AllyCode { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? GuildId { get; set; }
    public string? GuildName { get; set; }
    public int Level { get; set; }
    public long GalacticPower { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<RosterUnitDocument> Roster { get; set; } = [];
}

internal sealed class RosterUnitDocument
{
    public string Id { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public int Level { get; set; }
    public int Rarity { get; set; }
    public int GearTier { get; set; }
    public int RelicTier { get; set; }
    public int EquippedModCount { get; set; }
    public long GalacticPower { get; set; }
    public bool IsShip { get; set; }
    public int ZetaCount { get; set; }
    public int OmicronCount { get; set; }
}
