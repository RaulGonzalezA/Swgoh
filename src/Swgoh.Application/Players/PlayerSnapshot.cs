namespace Swgoh.Application.Players;

public sealed record PlayerSnapshot(
    string Id,
    long AllyCode,
    DateTimeOffset CapturedAtUtc,
    long GalacticPower,
    long CharacterGalacticPower,
    long ShipGalacticPower,
    int CharacterCount,
    int ShipCount,
    int RelicCharacters,
    int Relic7Plus,
    int Relic8Plus,
    int Relic9Plus,
    int Relic10,
    int Zetas,
    int Omicrons);
