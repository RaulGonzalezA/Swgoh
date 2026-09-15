namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class GacDefenseStrategyDocument
{
    public string Id { get; set; } = string.Empty;
    public long AllyCode { get; set; }
    public int Format { get; set; }
    public List<GacDefenseTemplateSlotDocument> Slots { get; set; } = [];
    public List<string> ReservedAttackPresetIds { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class GacDefenseTemplateSlotDocument
{
    public int Position { get; set; }
    public string Zone { get; set; } = string.Empty;
    public string? PinnedTeamPresetId { get; set; }
}
