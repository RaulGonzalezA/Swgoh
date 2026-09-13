namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class GacHistoryRoundDocument
{
    public string Id { get; set; } = string.Empty;
    public long AllyCode { get; set; }
    public int Season { get; set; }
    public int EventNumber { get; set; }
    public int RoundNumber { get; set; }
    public int Format { get; set; }
    public int League { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public bool? FullClear { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<GacDefensePlacementDocument> Defenses { get; set; } = [];
    public List<GacOffenseBattleDocument> OffenseBattles { get; set; } = [];
}

internal sealed class GacHistoricalSquadDocument
{
    public string LeaderDefinitionId { get; set; } = string.Empty;
    public List<string> MemberDefinitionIds { get; set; } = [];
    public bool IsFleet { get; set; }
}

internal sealed class GacDefensePlacementDocument
{
    public string Zone { get; set; } = string.Empty;
    public GacHistoricalSquadDocument Squad { get; set; } = new();
    public int Holds { get; set; }
    public bool Defeated { get; set; }
}

internal sealed class GacOffenseBattleDocument
{
    public string Zone { get; set; } = string.Empty;
    public GacHistoricalSquadDocument Defender { get; set; } = new();
    public GacHistoricalSquadDocument Attacker { get; set; } = new();
    public bool Won { get; set; }
    public int Banners { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset? AttackedAtUtc { get; set; }
}
