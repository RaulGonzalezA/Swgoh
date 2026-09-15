namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class GacGeneratedTeamLifecycleDocument
{
    public string Id { get; set; } = string.Empty;
    public long AllyCode { get; set; }
    public int Format { get; set; }
    public int Origin { get; set; }
    public string GenerationId { get; set; } = string.Empty;
    public string? RoundPlanId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
