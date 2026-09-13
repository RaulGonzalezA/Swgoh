namespace Swgoh.Infrastructure.Persistence.Documents;

internal sealed class ConquestPlanDocument
{
    public string Id { get; set; } = string.Empty;
    public long AllyCode { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Difficulty { get; set; }
    public List<ConquestFeatDocument> Feats { get; set; } = [];
    public int? StaminaCostPerBattle { get; set; }
    public int? ReserveFloorPercent { get; set; }
    public List<ConquestUnitStaminaDocument> Stamina { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class ConquestFeatDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Scope { get; set; }
    public int? Sector { get; set; }
    public int Points { get; set; }
    public int Target { get; set; }
    public int Progress { get; set; }
    public int ExpectedProgressPerBattle { get; set; }
    public ConquestFeatRuleDocument Rule { get; set; } = new();
}

internal sealed class ConquestFeatRuleDocument
{
    public int Type { get; set; }
    public string? Faction { get; set; }
    public List<string> UnitDefinitionIds { get; set; } = [];
    public int MinimumMatchingUnits { get; set; }
}

internal sealed class ConquestUnitStaminaDocument
{
    public string DefinitionId { get; set; } = string.Empty;
    public int CurrentPercent { get; set; }
}
