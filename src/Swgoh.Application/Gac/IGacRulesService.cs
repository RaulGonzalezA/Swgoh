using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacRulesService
{
    GacDefenseRequirements GetDefenseRequirements(GacLeague league, GacFormat format);

    GacLeagueTransition CompareLeagues(GacLeague fromLeague, GacLeague toLeague, GacFormat format);
}
