using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

internal static class PlayerRosterMetrics
{
    public static PlayerRosterAnalysis Calculate(PlayerProfile player)
    {
        RosterUnit[] characters = [.. player.Roster.Where(unit => !unit.IsShip)];
        RosterUnit[] ships = [.. player.Roster.Where(unit => unit.IsShip)];

        long characterGalacticPower = characters.Sum(unit => unit.GalacticPower);
        long shipGalacticPower = ships.Sum(unit => unit.GalacticPower);

        return new PlayerRosterAnalysis(
            player.AllyCode,
            player.GalacticPower,
            characterGalacticPower,
            shipGalacticPower,
            characters.Length,
            ships.Length,
            characters.Count(unit => unit.RelicTier > 0),
            characters.Count(unit => unit.RelicTier >= 7),
            characters.Count(unit => unit.RelicTier >= 8),
            characters.Count(unit => unit.RelicTier >= 9),
            characters.Count(unit => unit.RelicTier >= 10),
            player.Roster.Sum(unit => unit.ZetaCount),
            player.Roster.Sum(unit => unit.OmicronCount),
            characters.Count(unit => unit.EquippedModCount >= 6),
            characters.Count(unit => unit.EquippedModCount == 0),
            player.UpdatedAtUtc);
    }

    public static PlayerSnapshot CreateSnapshot(PlayerProfile player)
    {
        PlayerRosterAnalysis metrics = Calculate(player);
        return new PlayerSnapshot(
            $"{player.AllyCode}:{player.UpdatedAtUtc.UtcDateTime.Ticks}",
            player.AllyCode,
            player.UpdatedAtUtc,
            metrics.GalacticPower,
            metrics.CharacterGalacticPower,
            metrics.ShipGalacticPower,
            metrics.CharacterCount,
            metrics.ShipCount,
            metrics.RelicCharacters,
            metrics.Relic7Plus,
            metrics.Relic8Plus,
            metrics.Relic9Plus,
            metrics.Relic10,
            metrics.Zetas,
            metrics.Omicrons);
    }
}
