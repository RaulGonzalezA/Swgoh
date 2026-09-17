using MongoDB.Bson.Serialization.Attributes;

namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class PlayerInventoryDocument
{
    [BsonId]
    public long AllyCode { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<PlayerInventoryResourceDocument> Resources { get; set; } = [];
}

internal sealed class PlayerInventoryResourceDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Quantity { get; set; }
}
