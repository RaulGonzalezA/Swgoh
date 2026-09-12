using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Api.Endpoints;

internal static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/players").WithTags("Players");
        group.MapGet("/{allyCode:long}", GetAsync);
        group.MapGet("/{allyCode:long}/analysis", GetAnalysisAsync);
        group.MapGet("/{allyCode:long}/history", GetHistoryAsync);
        group.MapPost("/{allyCode:long}/refresh", RefreshAsync);
        group.MapPut("/{allyCode:long}", PutAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(long allyCode, IPlayerProfileService service, CancellationToken cancellationToken)
    {
        PlayerProfile? player = await service.GetAsync(allyCode, cancellationToken);
        return player is null ? Results.NotFound() : Results.Ok(PlayerResponse.From(player));
    }

    private static async Task<IResult> GetAnalysisAsync(
        long allyCode,
        IPlayerAnalysisService service,
        CancellationToken cancellationToken)
    {
        PlayerRosterAnalysis? analysis = await service.GetAsync(allyCode, cancellationToken);
        return analysis is null ? Results.NotFound() : Results.Ok(analysis);
    }

    private static async Task<IResult> GetHistoryAsync(
        long allyCode,
        int? limit,
        IPlayerHistoryService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<PlayerSnapshot> snapshots = await service.GetRecentAsync(allyCode, limit ?? 30, cancellationToken);
        return Results.Ok(snapshots);
    }

    private static async Task<IResult> RefreshAsync(
        long allyCode,
        IPlayerProfileService service,
        CancellationToken cancellationToken)
    {
        try
        {
            PlayerProfile player = await service.RefreshFromGameAsync(allyCode, cancellationToken);
            return Results.Ok(PlayerResponse.From(player));
        }
        catch (HttpRequestException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["player"] = [exception.Message] });
        }
    }

    private static async Task<IResult> PutAsync(
        long allyCode,
        SavePlayerRequest request,
        IPlayerProfileService service,
        CancellationToken cancellationToken)
    {
        try
        {
            PlayerProfile player = await service.SaveAsync(allyCode, request.Name, request.GalacticPower, cancellationToken);
            return Results.Ok(PlayerResponse.From(player));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["player"] = [exception.Message] });
        }
    }

    internal sealed record SavePlayerRequest(string Name, long GalacticPower);

    internal sealed record PlayerResponse(
        long AllyCode,
        string PlayerId,
        string Name,
        string? GuildId,
        string? GuildName,
        int Level,
        long GalacticPower,
        DateTimeOffset UpdatedAtUtc,
        int RosterCount,
        IReadOnlyCollection<RosterUnitResponse> Roster)
    {
        public static PlayerResponse From(PlayerProfile player) => new(
            player.AllyCode,
            player.PlayerId,
            player.Name,
            player.GuildId,
            player.GuildName,
            player.Level,
            player.GalacticPower,
            player.UpdatedAtUtc,
            player.Roster.Count,
            [.. player.Roster.Select(RosterUnitResponse.From)]);
    }

    internal sealed record RosterUnitResponse(
        string Id,
        string DefinitionId,
        int Level,
        int Rarity,
        int GearTier,
        int RelicTier,
        int EquippedModCount,
        long GalacticPower,
        bool IsShip,
        int ZetaCount,
        int OmicronCount)
    {
        public static RosterUnitResponse From(RosterUnit unit) => new(
            unit.Id,
            unit.DefinitionId,
            unit.Level,
            unit.Rarity,
            unit.GearTier,
            unit.RelicTier,
            unit.EquippedModCount,
            unit.GalacticPower,
            unit.IsShip,
            unit.ZetaCount,
            unit.OmicronCount);
    }
}
