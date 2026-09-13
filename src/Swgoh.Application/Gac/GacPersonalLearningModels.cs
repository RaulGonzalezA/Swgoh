using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public sealed record GacPersonalAttackOutcome(
    Guid AttackId,
    int Attempt,
    GacAttackPlanStatus Status,
    GacPlannerSquad DefenseSquad,
    GacPlannerSquad AttackSquad);

public sealed record GacPersonalRoundOutcome(
    string Id,
    long AllyCode,
    long OpponentAllyCode,
    string EventInstanceId,
    int RoundNumber,
    GacFormat Format,
    IReadOnlyCollection<GacPersonalAttackOutcome> Attacks,
    DateTimeOffset UpdatedAtUtc);

internal sealed record GacPersonalLearningSignal(
    decimal Adjustment,
    int Samples,
    int Wins,
    int Failures,
    decimal? WinRate,
    string Scope,
    string Summary)
{
    public static GacPersonalLearningSignal None { get; } = new(
        0m,
        0,
        0,
        0,
        null,
        "None",
        "Sin histórico personal suficiente para ajustar este counter.");
}
