using MongoDB.Bson.Serialization.Attributes;

namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class PlayerSnapshotDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public long AllyCode { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public long GalacticPower { get; set; }
    public long CharacterGalacticPower { get; set; }
    public long ShipGalacticPower { get; set; }
    public int CharacterCount { get; set; }
    public int ShipCount { get; set; }
    public int RelicCharacters { get; set; }
    public int Relic7Plus { get; set; }
    public int Relic8Plus { get; set; }
    public int Relic9Plus { get; set; }
    public int Relic10 { get; set; }
    public int Zetas { get; set; }
    public int Omicrons { get; set; }
}
