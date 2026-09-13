using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class GacRulesService : IGacRulesService
{
    public GacDefenseRequirements GetDefenseRequirements(GacLeague league, GacFormat format) =>
        GacDefenseRules.GetRequirements(league, format);

    public GacLeagueTransition CompareLeagues(
        GacLeague fromLeague,
        GacLeague toLeague,
        GacFormat format) =>
        GacDefenseRules.Compare(fromLeague, toLeague, format);
}
