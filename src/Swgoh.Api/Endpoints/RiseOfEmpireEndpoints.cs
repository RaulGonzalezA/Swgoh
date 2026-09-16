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
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["allyCode"] = [exception.Message]
            });
        }
    }
}
