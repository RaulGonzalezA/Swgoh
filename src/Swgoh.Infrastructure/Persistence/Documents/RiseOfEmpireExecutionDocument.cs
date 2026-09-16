namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class RiseOfEmpireExecutionDocument
{
    public string Id { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string GuildName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public List<RiseOfEmpireMissionExecutionResultDocument> Results { get; set; } = [];
}

internal sealed class RiseOfEmpireMissionExecutionResultDocument
{
    public long PlayerAllyCode { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int Phase { get; set; }
    public string PlanetId { get; set; } = string.Empty;
    public string PlanetName { get; set; } = string.Empty;
    public string MissionId { get; set; } = string.Empty;
    public string MissionName { get; set; } = string.Empty;
    public string? TeamName { get; set; }
    public int State { get; set; }
    public int CompletedWaves { get; set; }
    public int TotalWaves { get; set; }
    public long? TerritoryPoints { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
