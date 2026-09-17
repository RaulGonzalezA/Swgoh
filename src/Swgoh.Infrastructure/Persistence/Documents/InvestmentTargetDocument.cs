using MongoDB.Bson.Serialization.Attributes;

namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class InvestmentTargetDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public long AllyCode { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int? TargetRelicTier { get; set; }
    public int? TargetStars { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
