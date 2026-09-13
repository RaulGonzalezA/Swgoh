namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class GacPersonalBattleDocument
{
    public string Id { get; set; } = string.Empty;
    public long PlayerAllyCode { get; set; }
    public long OpponentAllyCode { get; set; }
    public string EventInstanceId { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public int Format { get; set; }
    public string AttackId { get; set; } = string.Empty;
    public string DefenseId { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public bool IsFleet { get; set; }
    public List<string> AttackerDefinitionIds { get; set; } = [];
    public List<string> DefenderDefinitionIds { get; set; } = [];
    public bool Won { get; set; }
    public int? Banners { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
