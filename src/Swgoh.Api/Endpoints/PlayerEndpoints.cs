using Asp.Versioning;

using Microsoft.AspNetCore.RateLimiting;

using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Api.Endpoints;

internal static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("Players");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/players")
            .HasApiVersion(1.0)
            .WithTags("Players");

        group.MapGet("/{allyCode:long}", GetAsync)
            .WithSummary("Get the persisted player profile");
        group.MapGet("/{allyCode:long}/roster", GetRosterAsync)
            .WithSummary("Get a filtered, sorted and paged player roster");
        group.MapGet("/{allyCode:long}/analysis", GetAnalysisAsync)
            .WithSummary("Get roster analysis metrics");
        group.MapGet("/{allyCode:long}/history", GetHistoryAsync)
            .WithSummary("Get recent player snapshots");
        group.MapGet("/{allyCode:long}/gl-progress", GetGalacticLegendProgressAsync)
            .WithSummary("Get Galactic Legend requirement progress");
        group.MapPost("/{allyCode:long}/refresh", RefreshAsync)
            .WithSummary("Refresh a player from live SWGOH data")
            .RequireRateLimiting("player-refresh")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPut("/{allyCode:long}", PutAsync)
            .WithSummary("Create or update a player manually");

        return endpoints;
    }

    private static async Task<IResult> GetAsync(long allyCode, IPlayerProfileService service, CancellationToken cancellationToken)
    {
        PlayerProfile? player = await service.GetAsync(allyCode, cancellationToken);
        return player is null ? Results.NotFound() : Results.Ok(PlayerResponse.From(player));
    }

    private static async Task<IResult> GetRosterAsync(
        long allyCode,
        int? page,
        int? pageSize,
        string? search,
        PlayerRosterUnitType? type,
        int? minRarity,
        int? minRelic,
        bool? hasZeta,
        bool? hasOmicron,
        PlayerRosterSortField? orderBy,
        PlayerRosterSortDirection? direction,
        IPlayerRosterService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new PlayerRosterQuery(
                page ?? 1,
                pageSize ?? 50,
                search,
                type ?? PlayerRosterUnitType.All,
                minRarity,
                minRelic,
                hasZeta,
                hasOmicron,
                orderBy ?? PlayerRosterSortField.GalacticPower,
                direction ?? PlayerRosterSortDirection.Descending);

            PlayerRosterPage? roster = await service.GetAsync(allyCode, query, cancellationToken);
            return roster is null ? Results.NotFound() : Results.Ok(RosterPageResponse.From(roster));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["roster"] = [exception.Message] });
        }
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

    private static async Task<IResult> GetGalacticLegendProgressAsync(
        long allyCode,
        IGalacticLegendProgressService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<GalacticLegendProgress>? progress = await service.GetAsync(allyCode, cancellationToken);
        return progress is null ? Results.NotFound() : Results.Ok(progress);
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

    internal sealed record RosterPageResponse(
        long AllyCode,
        DateTimeOffset UpdatedAtUtc,
        int Total,
        int Page,
        int PageSize,
        int TotalPages,
        IReadOnlyCollection<RosterUnitResponse> Items)
    {
        public static RosterPageResponse From(PlayerRosterPage roster) => new(
            roster.AllyCode,
            roster.UpdatedAtUtc,
            roster.Total,
            roster.Page,
            roster.PageSize,
            roster.TotalPages,
            [.. roster.Items.Select(RosterUnitResponse.From)]);
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
