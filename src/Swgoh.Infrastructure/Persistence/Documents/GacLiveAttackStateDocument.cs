namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class GacLiveAttackStateDocument
{
    public string Id { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public long PlayerAllyCode { get; set; }
    public string AttackId { get; set; } = string.Empty;
    public string DefenseId { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public int Status { get; set; }
    public int? Banners { get; set; }
    public string? Notes { get; set; }
    public List<string> RemainingEnemyUnitDefinitionIds { get; set; } = [];
    public bool PreloadedTurnMeter { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
