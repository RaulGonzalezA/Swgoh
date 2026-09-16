using Asp.Versioning;

using Swgoh.Application.TerritoryBattles;

namespace Swgoh.Api.Endpoints;

internal static class RiseOfEmpireEndpoints
{
    public static IEndpointRouteBuilder MapRiseOfEmpireEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("RiseOfEmpire");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/territory-battles/players/{allyCode:long}/rise-of-the-empire")
            .HasApiVersion(1.0)
            .WithTags("Territory Battles");

        group.MapGet("/analysis", GetAnalysisAsync)
            .WithSummary("Analyze Rise of the Empire planets, recommended teams and roster upgrade priorities");
        group.MapGet("/guild", GetGuildAsync)
            .WithSummary("Analyze Rise of the Empire for the cached guild rosters");
        group.MapPost("/guild/sync", SyncGuildAsync)
            .RequireRateLimiting("player-refresh")
            .WithSummary("Import the current guild rosters and build a Rise of the Empire guild plan");
        return endpoints;
    }

    private static async Task<IResult> GetAnalysisAsync(
        long allyCode,
        IRiseOfEmpireService service,
        CancellationToken cancellationToken)
    {
        try
        {
            RiseOfEmpireAnalysis? analysis = await service.GetAsync(allyCode, cancellationToken);
            return analysis is null ? Results.NotFound() : Results.Ok(analysis);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Validation(exception);
        }
    }

    private static Task<IResult> GetGuildAsync(
        long allyCode,
        IRiseOfEmpireGuildService service,
        CancellationToken cancellationToken) =>
        GetGuildCoreAsync(allyCode, refreshGuildRoster: false, service, cancellationToken);

    private static Task<IResult> SyncGuildAsync(
        long allyCode,
        IRiseOfEmpireGuildService service,
        CancellationToken cancellationToken) =>
        GetGuildCoreAsync(allyCode, refreshGuildRoster: true, service, cancellationToken);

    private static async Task<IResult> GetGuildCoreAsync(
        long allyCode,
        bool refreshGuildRoster,
        IRiseOfEmpireGuildService service,
        CancellationToken cancellationToken)
    {
        try
        {
            RiseOfEmpireGuildAnalysis? analysis = await service
                .GetAsync(allyCode, refreshGuildRoster, cancellationToken)
                .ConfigureAwait(false);
            return analysis is null ? Results.NotFound() : Results.Ok(analysis);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Validation(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Guild data unavailable",
                detail: exception.Message);
        }
    }

    private static IResult Validation(ArgumentOutOfRangeException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["allyCode"] = [exception.Message]
        });
}
