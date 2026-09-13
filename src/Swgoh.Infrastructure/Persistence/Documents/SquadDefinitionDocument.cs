namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class SquadDefinitionDocument
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Format { get; set; }
    public int Use { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<SquadVariantDocument> Variants { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class SquadVariantDocument
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LeaderDefinitionId { get; set; } = string.Empty;
    public List<string> MemberDefinitionIds { get; set; } = [];
}
