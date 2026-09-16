namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class RiseOfEmpireGuildSyncJobDocument
{
    public string Id { get; set; } = string.Empty;

    public long AllyCode { get; set; }

    public int Status { get; set; }

    public int TotalMembers { get; set; }

    public int CompletedMembers { get; set; }

    public int FailedMembers { get; set; }

    public string? CurrentMember { get; set; }

    public string? GuildId { get; set; }

    public string? GuildName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? Error { get; set; }
}
