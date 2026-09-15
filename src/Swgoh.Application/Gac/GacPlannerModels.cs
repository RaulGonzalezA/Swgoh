using Swgoh.Domain.Gac;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Gac;

public sealed record SaveGacTeamPreset(
    string Name,
    GacFormat Format,
    GacPlannerTeamUse Use,
    string LeaderDefinitionId,
    IReadOnlyCollection<string> MemberDefinitionIds,
    bool IsFleet);

public sealed record SaveGacOwnDefenseAssignment(
    Guid? Id,
    string Zone,
    Guid TeamPresetId,
    string? DatacronId = null);

public sealed record SaveGacVisibleDefense(
    Guid? Id,
    string Zone,
    string? Label,
    string LeaderDefinitionId,
    IReadOnlyCollection<string> MemberDefinitionIds,
    bool IsFleet);

public sealed record SaveGacAttackAssignment(
    Guid? Id,
    Guid DefenseId,
    Guid TeamPresetId,
    int Attempt,
    GacAttackPlanStatus Status,
    string? Notes,
    string? DatacronId = null);

public sealed record SaveCurrentGacRoundPlan(
    IReadOnlyCollection<SaveGacOwnDefenseAssignment> OwnDefenses,
    IReadOnlyCollection<SaveGacVisibleDefense> VisibleDefenses,
    IReadOnlyCollection<SaveGacAttackAssignment> Attacks,
    long? ExpectedVersion = null);

public sealed record GacPlannerUnitDetails(
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    bool IsShip,
    long? GalacticPower,
    int? RelicTier,
    int? ZetaCount,
    int? OmicronCount,
    RosterUnitStats? Stats = null,
    RosterModSummary? Mods = null);

public sealed record GacPlannerSquadDetails(
    GacPlannerUnitDetails Leader,
    IReadOnlyCollection<GacPlannerUnitDetails> Members,
    bool IsFleet)
{
    public IReadOnlyCollection<GacPlannerUnitDetails> AllUnits => [Leader, .. Members];
}

public sealed record GacTeamPresetDetails(
    Guid Id,
    long AllyCode,
    string Name,
    GacFormat Format,
    GacPlannerTeamUse Use,
    GacPlannerSquadDetails Squad,
    DateTimeOffset UpdatedAtUtc);

public sealed record GacOwnDefenseAssignmentDetails(
    Guid Id,
    string Zone,
    GacTeamPresetDetails Team,
    string? DatacronId = null);

public sealed record GacVisibleDefenseDetails(
    Guid Id,
    string Zone,
    string? Label,
    GacPlannerSquadDetails Squad,
    bool Defeated);

public sealed record GacAttackAssignmentDetails(
    Guid Id,
    Guid DefenseId,
    GacTeamPresetDetails Team,
    int Attempt,
    GacAttackPlanStatus Status,
    string? Notes,
    int? Banners = null,
    string? DatacronId = null);

public sealed record GacPlannerConflict(
    string Code,
    string Severity,
    string Message,
    IReadOnlyCollection<Guid> RelatedAssignmentIds,
    IReadOnlyCollection<string> UnitDefinitionIds);

public sealed record GacPlannerCounterHint(
    Guid DefenseId,
    string ThreatName,
    string Confidence,
    string Source,
    string Rationale,
    bool RequiresDatacronVerification,
    Guid? MatchingTeamPresetId,
    IReadOnlyCollection<GacPlannerUnitDetails> RecommendedTeam,
    int? Uses,
    decimal? WinRate,
    decimal? OneShotRate,
    decimal? AverageBanners,
    int? PlayersObserved);

public sealed record GacPlannerDatacronDetails(
    string Id,
    string SetId,
    string TemplateId,
    int Tier,
    bool Locked,
    int HighestRequiredRelicTier,
    bool HasAbilityAffix,
    IReadOnlyCollection<GacPlannerDatacronAffixDetails> Affixes,
    DateTimeOffset? ExpiresAtUtc = null)
{
    public bool IsExpired(DateTimeOffset nowUtc) =>
        ExpiresAtUtc is DateTimeOffset expiresAtUtc && expiresAtUtc <= nowUtc;
}

public sealed record GacPlannerDatacronAffixDetails(
    string? AbilityId,
    int? StatType,
    long? StatValue,
    int? RequiredRelicTier,
    IReadOnlyCollection<string> Tags);

public sealed record GacRoundPlanDetails(
    string Id,
    long PlayerAllyCode,
    long OpponentAllyCode,
    string OpponentName,
    string EventId,
    string EventInstanceId,
    int RoundNumber,
    GacFormat Format,
    GacLeague League,
    IReadOnlyCollection<GacOwnDefenseAssignmentDetails> OwnDefenses,
    IReadOnlyCollection<GacVisibleDefenseDetails> VisibleDefenses,
    IReadOnlyCollection<GacAttackAssignmentDetails> Attacks,
    IReadOnlyCollection<GacPlannerConflict> Conflicts,
    IReadOnlyCollection<GacPlannerCounterHint> CounterHints,
    DateTimeOffset UpdatedAtUtc,
    long Version = 0);

public sealed record GacPlannerState(
    CurrentGacOpponent Opponent,
    IReadOnlyCollection<GacTeamPresetDetails> Presets,
    GacRoundPlanDetails Plan,
    IReadOnlyCollection<GacPlannerDatacronDetails>? Datacrons = null,
    IReadOnlyCollection<GacPlannerDatacronDetails>? OpponentDatacrons = null)
{
    public IReadOnlyCollection<GacPlannerDatacronDetails> PlayerDatacrons => Datacrons ?? [];
    public IReadOnlyCollection<GacPlannerDatacronDetails> RivalDatacrons => OpponentDatacrons ?? [];
}

public sealed record GacPlannerLookup(
    CurrentGacOpponentStatus Status,
    string? Message,
    GacPlannerState? State)
{
    public bool IsAvailable => Status == CurrentGacOpponentStatus.Found && State is not null;
}
