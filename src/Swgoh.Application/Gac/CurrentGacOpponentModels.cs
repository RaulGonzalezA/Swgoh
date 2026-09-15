using Swgoh.Application.Players;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public enum CurrentGacOpponentStatus
{
    Found = 1,
    NoActiveEvent = 2,
    PlayerNotJoined = 3,
    OpponentUnavailable = 4,
    FormatUnavailable = 5,
    Pending = 6
}

public sealed record CurrentGacOpponent(
    long PlayerAllyCode,
    long OpponentAllyCode,
    string OpponentName,
    string? OpponentPlayerId,
    GacLeague League,
    GacFormat Format,
    string EventId,
    string EventInstanceId,
    string BracketId,
    int? RoundNumber,
    string FormatSource,
    string OpponentResolutionMethod);

public sealed record CurrentGacOpponentLookup(
    CurrentGacOpponentStatus Status,
    CurrentGacOpponent? Opponent,
    string? Message)
{
    public static CurrentGacOpponentLookup Found(CurrentGacOpponent opponent)
    {
        ArgumentNullException.ThrowIfNull(opponent);
        return new CurrentGacOpponentLookup(CurrentGacOpponentStatus.Found, opponent, null);
    }

    public static CurrentGacOpponentLookup Unavailable(
        CurrentGacOpponentStatus status,
        string message) => new(status, null, message);
}

public sealed record CurrentOpponentRosterScouting(
    PlayerRosterAnalysis Analysis,
    IReadOnlyCollection<PlayerRosterUnit> GalacticLegends,
    IReadOnlyCollection<PlayerRosterUnit> TopCharacters,
    IReadOnlyCollection<PlayerRosterUnit> TopShips,
    IReadOnlyCollection<PlayerRosterUnit> OmicronCharacters);

public sealed record GacBattleUnit(
    string DefinitionId,
    string Name,
    long GalacticPower,
    int RelicTier,
    int ZetaCount,
    int OmicronCount,
    bool IsShip,
    bool IsGalacticLegend);

public sealed record GacBattleRosterComparison(
    long PlayerGalacticPower,
    long OpponentGalacticPower,
    long GalacticPowerDelta,
    int PlayerGalacticLegends,
    int OpponentGalacticLegends,
    int PlayerOmicronCharacters,
    int OpponentOmicronCharacters,
    int PlayerRelic7Plus,
    int OpponentRelic7Plus,
    int PlayerRelic9Plus,
    int OpponentRelic9Plus);

public sealed record GacBattleThreat(
    GacBattleUnit Unit,
    int Score,
    string Priority,
    string Category,
    string Reason);

public sealed record GacBattleDefensePrediction(
    string LeaderName,
    string LeaderDefinitionId,
    IReadOnlyCollection<string> MemberNames,
    bool IsFleet,
    decimal? Probability,
    string Confidence,
    string Source,
    string? SquadDefinitionName,
    string? VariantName);

public sealed record GacBattleAttackReserve(
    GacBattleUnit Unit,
    string Role,
    string Priority,
    string Reason);

public sealed record GacBattleCounterSuggestion(
    GacBattleUnit Threat,
    IReadOnlyCollection<GacBattleUnit> CandidateAnchors,
    string Confidence,
    string Source,
    string Rationale,
    bool RequiresDatacronVerification,
    IReadOnlyCollection<GacBattleUnit>? RecommendedTeam = null,
    int? Uses = null,
    decimal? WinRate = null,
    decimal? OneShotRate = null,
    decimal? AverageBanners = null,
    int? PlayersObserved = null);

public sealed record CurrentGacBattlePlan(
    GacBattleRosterComparison Comparison,
    IReadOnlyCollection<GacBattleThreat> Threats,
    IReadOnlyCollection<GacBattleDefensePrediction> DefensePredictions,
    IReadOnlyCollection<GacBattleAttackReserve> AttackReserves,
    IReadOnlyCollection<GacBattleCounterSuggestion> CounterSuggestions,
    IReadOnlyCollection<string> Warnings);

public sealed record CurrentGacScoutingResult(
    CurrentGacOpponentLookup Lookup,
    OpponentScoutingReport? Scouting,
    CurrentOpponentRosterScouting? RosterScouting,
    CurrentGacBattlePlan? BattlePlan,
    GacHistorySyncResult? HistorySync = null,
    IReadOnlyCollection<string>? Warnings = null)
{
    public IReadOnlyCollection<string> DegradationWarnings =>
    [
        .. (Warnings ?? []),
        .. (HistorySync?.Warnings ?? [])
    ];
}
