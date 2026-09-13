namespace Swgoh.Application.Players;

public sealed record PlayerRosterPage(
    long AllyCode,
    DateTimeOffset UpdatedAtUtc,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyCollection<PlayerRosterUnit> Items);
