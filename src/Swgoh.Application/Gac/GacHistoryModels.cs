using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public sealed record GacHistorySquadInput(
    string LeaderDefinitionId,
    IReadOnlyCollection<string> MemberDefinitionIds,
    bool IsFleet);

public sealed record GacHistoryDefenseInput(
    string Zone,
    GacHistorySquadInput Squad,
    int Holds,
    bool Defeated);

public sealed record GacHistoryOffenseBattleInput(
    string Zone,
    GacHistorySquadInput Defender,
    GacHistorySquadInput Attacker,
    bool Won,
    int Banners,
    int Attempt,
    DateTimeOffset? AttackedAtUtc);

public sealed record GacHistoryRoundInput(
    int Season,
    int EventNumber,
    int RoundNumber,
    GacFormat Format,
    GacLeague League,
    DateTimeOffset StartedAtUtc,
    bool? FullClear,
    string Source,
    IReadOnlyCollection<GacHistoryDefenseInput> Defenses,
    IReadOnlyCollection<GacHistoryOffenseBattleInput> OffenseBattles);

public sealed record GacHistoryImportResult(int ImportedRounds);

public sealed record GacHistoryQuery(GacFormat? Format = null, int MaxRounds = 30);

public sealed record ScoutingUnitDetails(
    string DefinitionId,
    string Name,
    bool IsGalacticLegend);

public sealed record ZoneFrequency(string Zone, int Count);

public sealed record GacDefensePatternDetails(
    string CompositionKey,
    bool IsFleet,
    ScoutingUnitDetails Leader,
    IReadOnlyCollection<ScoutingUnitDetails> Members,
    Guid? SquadDefinitionId,
    string? SquadDefinitionName,
    string? VariantKey,
    string? VariantName,
    int Appearances,
    int RoundsPlaced,
    decimal PlacementRate,
    decimal AverageHolds,
    decimal HoldRate,
    IReadOnlyCollection<ZoneFrequency> Zones,
    string Confidence);

public sealed record GacCounterPatternDetails(
    bool IsFleet,
    ScoutingUnitDetails DefenderLeader,
    ScoutingUnitDetails AttackerLeader,
    int Uses,
    int Wins,
    decimal WinRate,
    int OneShots,
    decimal OneShotRate,
    decimal AverageBanners,
    decimal AverageAttempt);

public sealed record GacPredictedDefenseDetails(
    string CompositionKey,
    bool IsFleet,
    ScoutingUnitDetails Leader,
    IReadOnlyCollection<ScoutingUnitDetails> Members,
    decimal Probability,
    string Confidence,
    string? SquadDefinitionName,
    string? VariantName);

public sealed record OpponentScoutingReport(
    long AllyCode,
    GacFormat Format,
    int RoundsAnalyzed,
    int SeasonsAnalyzed,
    DateTimeOffset? EarliestRoundUtc,
    DateTimeOffset? LatestRoundUtc,
    GacLeague? LatestObservedLeague,
    GacLeague TargetLeague,
    int RequiredSquadDefenses,
    int RequiredFleetDefenses,
    int AdditionalUnobservedSquadSlots,
    int AdditionalUnobservedFleetSlots,
    decimal? FullClearRate,
    decimal? AverageFirstAttackDelayMinutes,
    IReadOnlyCollection<GacDefensePatternDetails> DefensePatterns,
    IReadOnlyCollection<GacCounterPatternDetails> CounterPatterns,
    IReadOnlyCollection<GacPredictedDefenseDetails> PredictedSquadDefenses,
    IReadOnlyCollection<GacPredictedDefenseDetails> PredictedFleetDefenses);
