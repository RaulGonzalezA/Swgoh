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
        group.MapGet("/mission-guides", GetMissionGuidesAsync)
            .WithSummary("Get concrete Rise of the Empire mission teams and fleets matched against the roster");
        group.MapGet("/guild", GetGuildAsync)
            .WithSummary("Analyze Rise of the Empire for the cached guild rosters");
        group.MapGet("/guild/sync", GetLatestGuildSyncAsync)
            .WithSummary("Get the latest Rise of the Empire guild synchronization job");
        group.MapGet("/guild/sync/{jobId}", GetGuildSyncAsync)
            .WithSummary("Get Rise of the Empire guild synchronization progress");
        group.MapPost("/guild/sync", StartGuildSyncAsync)
            .RequireRateLimiting("player-refresh")
            .WithSummary("Queue a background import of the current guild rosters");
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

    private static async Task<IResult> GetMissionGuidesAsync(
        long allyCode,
        IRiseOfEmpireMissionGuideService service,
        CancellationToken cancellationToken)
    {
        try
        {
            RiseOfEmpireMissionGuideAnalysis? analysis = await service.GetAsync(allyCode, cancellationToken);
            return analysis is null ? Results.NotFound() : Results.Ok(analysis);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GetGuildAsync(
        long allyCode,
        IRiseOfEmpireGuildService service,
        CancellationToken cancellationToken)
    {
        try
        {
            RiseOfEmpireGuildAnalysis? analysis = await service
                .GetAsync(allyCode, refreshGuildRoster: false, cancellationToken)
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

    private static async Task<IResult> StartGuildSyncAsync(
        long allyCode,
        IRiseOfEmpireGuildSyncQueue queue,
        CancellationToken cancellationToken)
    {
        try
        {
            RiseOfEmpireGuildSyncJob job = await queue.StartAsync(allyCode, cancellationToken).ConfigureAwait(false);
            string location = $"/api/v1/territory-battles/players/{allyCode}/rise-of-the-empire/guild/sync/{Uri.EscapeDataString(job.Id)}";
            return Results.Accepted(location, job);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GetLatestGuildSyncAsync(
        long allyCode,
        IRiseOfEmpireGuildSyncQueue queue,
        CancellationToken cancellationToken)
    {
        RiseOfEmpireGuildSyncJob? job = await queue.GetLatestAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return job is null ? Results.NotFound() : Results.Ok(job);
    }

    private static async Task<IResult> GetGuildSyncAsync(
        long allyCode,
        string jobId,
        IRiseOfEmpireGuildSyncQueue queue,
        CancellationToken cancellationToken)
    {
        RiseOfEmpireGuildSyncJob? job = await queue.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        return job is null || job.AllyCode != allyCode ? Results.NotFound() : Results.Ok(job);
    }

    private static IResult Validation(ArgumentOutOfRangeException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["allyCode"] = [exception.Message]
        });
}
