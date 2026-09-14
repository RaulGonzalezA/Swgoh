using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public sealed record GacBracketLocation(
    long AllyCode,
    string EventId,
    string EventInstanceId,
    GacLeague League,
    GacFormat Format,
    int BracketIndex,
    int SkillRating,
    DateTimeOffset FoundAtUtc)
{
    public string Id => CreateId(AllyCode, EventInstanceId, League);

    public static string CreateId(long allyCode, string eventInstanceId, GacLeague league) =>
        $"{allyCode}:{eventInstanceId}:{league}";
}

public interface IGacBracketLocationRepository
{
    Task<GacBracketLocation?> FindAsync(
        long allyCode,
        string eventInstanceId,
        GacLeague league,
        CancellationToken cancellationToken = default);

    Task<GacBracketLocation?> FindLatestAsync(
        long allyCode,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        GacBracketLocation location,
        CancellationToken cancellationToken = default);
}
