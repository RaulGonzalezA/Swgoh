namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class GacTeamPresetDocument
{
    public string Id { get; set; } = string.Empty;
    public long AllyCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Format { get; set; }
    public int Use { get; set; }
    public GacPlannerSquadDocument Squad { get; set; } = new();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class GacRoundPlanDocument
{
    public string Id { get; set; } = string.Empty;
    public long PlayerAllyCode { get; set; }
    public long OpponentAllyCode { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string EventInstanceId { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public int Format { get; set; }
    public int League { get; set; }
    public List<GacOwnDefenseAssignmentDocument> OwnDefenses { get; set; } = [];
    public List<GacVisibleDefenseDocument> VisibleDefenses { get; set; } = [];
    public List<GacAttackAssignmentDocument> Attacks { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

internal sealed class GacPlannerSquadDocument
{
    public string LeaderDefinitionId { get; set; } = string.Empty;
    public List<string> MemberDefinitionIds { get; set; } = [];
    public bool IsFleet { get; set; }
}

internal sealed class GacOwnDefenseAssignmentDocument
{
    public string Id { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string TeamPresetId { get; set; } = string.Empty;
    public string? DatacronId { get; set; }
}

internal sealed class GacVisibleDefenseDocument
{
    public string Id { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string? Label { get; set; }
    public GacPlannerSquadDocument Squad { get; set; } = new();
}

internal sealed class GacAttackAssignmentDocument
{
    public string Id { get; set; } = string.Empty;
    public string DefenseId { get; set; } = string.Empty;
    public string TeamPresetId { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public int Status { get; set; }
    public string? Notes { get; set; }
    public string? DatacronId { get; set; }
}
