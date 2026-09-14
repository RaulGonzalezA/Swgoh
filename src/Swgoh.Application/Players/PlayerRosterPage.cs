namespace Swgoh.Application.Players;

public sealed record PlayerRosterPage(
    long AllyCode,
    DateTimeOffset UpdatedAtUtc,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyCollection<PlayerRosterUnit> Items,
    string? PlayerName = null,
    long GalacticPower = 0,
    int RosterCount = 0,
    IReadOnlyCollection<string>? AvailableFactions = null)
{
    public IReadOnlyCollection<string> FactionOptions => AvailableFactions ?? [];
}
