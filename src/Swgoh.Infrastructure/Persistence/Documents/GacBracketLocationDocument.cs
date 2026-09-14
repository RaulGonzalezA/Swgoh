namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class GacBracketLocationDocument
{
    public string Id { get; set; } = string.Empty;
    public long AllyCode { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string EventInstanceId { get; set; } = string.Empty;
    public int League { get; set; }
    public int Format { get; set; }
    public int BracketIndex { get; set; }
    public int SkillRating { get; set; }
    public DateTimeOffset FoundAtUtc { get; set; }
}
