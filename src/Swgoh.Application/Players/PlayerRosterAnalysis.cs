namespace Swgoh.Application.Players;

public sealed record PlayerRosterAnalysis(
    long AllyCode,
    long GalacticPower,
    long CharacterGalacticPower,
    long ShipGalacticPower,
    int CharacterCount,
    int ShipCount,
    int RelicCharacters,
    int Relic7Plus,
    int Relic8Plus,
    int Relic9Plus,
    int Zetas,
    int Omicrons,
    int FullyModdedCharacters,
    int UnmoddedCharacters,
    DateTimeOffset UpdatedAtUtc);
