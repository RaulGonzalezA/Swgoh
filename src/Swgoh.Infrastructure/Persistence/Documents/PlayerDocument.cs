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
    public List<PlayerDatacronDocument> Datacrons { get; set; } = [];
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
    public RosterUnitStatsDocument? Stats { get; set; }
    public RosterModSummaryDocument? Mods { get; set; }
}

internal sealed class RosterUnitStatsDocument
{
    public decimal? Health { get; set; }
    public decimal? Protection { get; set; }
    public decimal? Speed { get; set; }
    public decimal? PhysicalDamage { get; set; }
    public decimal? SpecialDamage { get; set; }
    public decimal? Armor { get; set; }
    public decimal? Resistance { get; set; }
    public decimal? Potency { get; set; }
    public decimal? Tenacity { get; set; }
    public decimal? CriticalDamage { get; set; }
}

internal sealed class RosterModSummaryDocument
{
    public int EquippedCount { get; set; }
    public int SixDotCount { get; set; }
    public int SpeedSetModCount { get; set; }
    public int SpeedPrimaryCount { get; set; }
    public decimal? SpeedBonus { get; set; }
}

internal sealed class PlayerDatacronDocument
{
    public string Id { get; set; } = string.Empty;
    public string SetId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int Tier { get; set; }
    public bool Locked { get; set; }
    public List<PlayerDatacronAffixDocument> Affixes { get; set; } = [];
}

internal sealed class PlayerDatacronAffixDocument
{
    public string? AbilityId { get; set; }
    public int? StatType { get; set; }
    public long? StatValue { get; set; }
    public int? RequiredRelicTier { get; set; }
    public List<string> Tags { get; set; } = [];
}
