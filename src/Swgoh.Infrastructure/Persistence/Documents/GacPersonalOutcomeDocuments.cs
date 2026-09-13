namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class GacPersonalRoundOutcomeDocument
{
    public string Id { get; set; } = string.Empty;
    public long AllyCode { get; set; }
    public long OpponentAllyCode { get; set; }
    public string EventInstanceId { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public int Format { get; set; }
    public List<GacPersonalAttackOutcomeDocument> Attacks { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class GacPersonalAttackOutcomeDocument
{
    public string AttackId { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public int Status { get; set; }
    public GacPlannerSquadDocument DefenseSquad { get; set; } = new();
    public GacPlannerSquadDocument AttackSquad { get; set; } = new();
}
