using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Gac;

internal static class GacDatacronRules
{
    public static GacPlannerDatacronDetails? BestEligible(
        GacTeamPresetDetails team,
        IEnumerable<GacPlannerDatacronDetails> datacrons,
        IReadOnlySet<string>? unavailableIds = null,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(datacrons);
        DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;

        return datacrons
            .Where(datacron => unavailableIds is null || !unavailableIds.Contains(datacron.Id))
            .Where(datacron => IsEligible(team, datacron, now))
            .OrderByDescending(datacron => datacron.Tier)
            .ThenByDescending(datacron => datacron.HasAbilityAffix)
            .ThenBy(datacron => datacron.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static bool IsEligible(
        GacTeamPresetDetails team,
        GacPlannerDatacronDetails datacron,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(datacron);
        DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;
        if (team.Squad.IsFleet || datacron.Tier < 3 || datacron.IsExpired(now))
        {
            return false;
        }

        int minimumRelic = team.Squad.AllUnits
            .Where(unit => !unit.IsShip)
            .Select(unit => unit.RelicTier ?? 0)
            .DefaultIfEmpty(0)
            .Min();
        return datacron.HighestRequiredRelicTier == 0 ||
            datacron.HighestRequiredRelicTier <= minimumRelic;
    }

    public static bool IsEligible(
        GacPlannerSquad squad,
        PlayerDatacron datacron,
        IReadOnlyDictionary<string, RosterUnit> roster,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(datacron);
        ArgumentNullException.ThrowIfNull(roster);
        DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;
        if (squad.IsFleet || datacron.Tier < 3 || datacron.IsExpired(now))
        {
            return false;
        }

        int minimumRelic = squad.AllUnitDefinitionIds
            .Select(id => roster.GetValueOrDefault(id)?.RelicTier ?? 0)
            .DefaultIfEmpty(0)
            .Min();
        return datacron.HighestRequiredRelicTier == 0 ||
            datacron.HighestRequiredRelicTier <= minimumRelic;
    }

    public static GacPlannerDatacronDetails ToDetails(PlayerDatacron datacron)
    {
        ArgumentNullException.ThrowIfNull(datacron);
        return new GacPlannerDatacronDetails(
            datacron.Id,
            datacron.SetId,
            datacron.TemplateId,
            datacron.Tier,
            datacron.Locked,
            datacron.HighestRequiredRelicTier,
            datacron.HasAbilityAffix,
            [
                .. datacron.Affixes.Select(affix => new GacPlannerDatacronAffixDetails(
                    affix.AbilityId,
                    affix.StatType,
                    affix.StatValue,
                    affix.RequiredRelicTier,
                    affix.Tags))
            ],
            datacron.ExpiresAtUtc);
    }

    public static bool IsActive(PlayerDatacron datacron, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(datacron);
        return !datacron.IsExpired(nowUtc ?? DateTimeOffset.UtcNow);
    }

    public static bool IsActive(GacPlannerDatacronDetails datacron, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(datacron);
        return !datacron.IsExpired(nowUtc ?? DateTimeOffset.UtcNow);
    }

    public static string? NormalizeId(string? datacronId) =>
        string.IsNullOrWhiteSpace(datacronId) ? null : datacronId.Trim();
}
