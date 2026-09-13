namespace Swgoh.Domain.Players;

public sealed record RosterUnitStats(
    decimal? Health = null,
    decimal? Protection = null,
    decimal? Speed = null,
    decimal? PhysicalDamage = null,
    decimal? SpecialDamage = null,
    decimal? Armor = null,
    decimal? Resistance = null,
    decimal? Potency = null,
    decimal? Tenacity = null,
    decimal? CriticalDamage = null);

public sealed record RosterModSummary(
    int EquippedCount,
    int SixDotCount,
    int SpeedSetModCount,
    int SpeedPrimaryCount,
    decimal? SpeedBonus = null)
{
    public bool IsComplete => EquippedCount >= 6;
}

public sealed record PlayerDatacron(
    string Id,
    string SetId,
    string TemplateId,
    int Tier,
    bool Locked,
    IReadOnlyCollection<PlayerDatacronAffix> Affixes)
{
    public int HighestRequiredRelicTier => Affixes
        .Where(affix => affix.RequiredRelicTier is not null)
        .Select(affix => affix.RequiredRelicTier!.Value)
        .DefaultIfEmpty(0)
        .Max();

    public bool HasAbilityAffix => Affixes.Any(affix => !string.IsNullOrWhiteSpace(affix.AbilityId));
}

public sealed record PlayerDatacronAffix(
    string? AbilityId,
    int? StatType,
    long? StatValue,
    int? RequiredRelicTier,
    IReadOnlyCollection<string> Tags);
