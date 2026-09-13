namespace Swgoh.Domain.Gac;

public sealed record GacDefenseRequirements(
    GacLeague League,
    GacFormat Format,
    int SquadDefenseCount,
    int FleetDefenseCount);

public sealed record GacLeagueTransition(
    GacDefenseRequirements From,
    GacDefenseRequirements To)
{
    public int SquadDefenseDelta => To.SquadDefenseCount - From.SquadDefenseCount;
    public int FleetDefenseDelta => To.FleetDefenseCount - From.FleetDefenseCount;
    public int AdditionalSquadDefenses => Math.Max(0, SquadDefenseDelta);
    public int AdditionalFleetDefenses => Math.Max(0, FleetDefenseDelta);
    public bool IsPromotion => To.League > From.League;
    public bool IsDemotion => To.League < From.League;
}
