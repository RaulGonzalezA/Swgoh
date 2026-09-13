using Asp.Versioning;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Api.Endpoints;

internal static class GacDataEndpoints
{
    public static IEndpointRouteBuilder MapGacDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("GAC Data");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/gac")
            .HasApiVersion(1.0)
            .WithTags("GAC");

        group.MapPost("/opponents/{allyCode:long}/history/sync", SyncHistoryAsync)
            .WithSummary("Synchronize opponent GAC history from configured authorized providers");
        group.MapGet("/counters", GetCountersAsync)
            .WithSummary("Get observed GAC counter statistics learned from imported history");

        return endpoints;
    }

    private static async Task<IResult> SyncHistoryAsync(
        long allyCode,
        string format,
        int? maxRounds,
        IGacHistorySyncService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacHistorySyncResult result = await service.SyncAsync(
                allyCode,
                ParseFormat(format),
                maxRounds ?? 30,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GetCountersAsync(
        string format,
        string? defenderLeader,
        bool? isFleet,
        int? maxRounds,
        int? limit,
        IGacCounterStatisticsService service,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyCollection<GacCounterStatistics> result = await service.GetAsync(
                new GacCounterStatisticsQuery(
                    ParseFormat(format),
                    defenderLeader,
                    isFleet,
                    maxRounds ?? 1_000,
                    limit ?? 100),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static GacFormat ParseFormat(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant() switch
        {
            "3" or "3v3" or "threevsthree" => GacFormat.ThreeVsThree,
            "5" or "5v5" or "fivevsfive" => GacFormat.FiveVsFive,
            _ => throw new ArgumentException("Format must be 3v3 or 5v5.", nameof(value))
        };
    }

    private static IResult Validation(ArgumentException exception) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["gac"] = [exception.Message] });
}
