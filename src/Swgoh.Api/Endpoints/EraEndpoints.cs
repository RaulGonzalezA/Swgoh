using Asp.Versioning;

using Swgoh.Application.Eras;

namespace Swgoh.Api.Endpoints;

internal static class EraEndpoints
{
    public static IEndpointRouteBuilder MapEraEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("Era");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/eras/players/{allyCode:long}")
            .HasApiVersion(1.0)
            .WithTags("Eras");

        group.MapGet("/current", GetCurrentAsync)
            .WithSummary("Analyze the current Era roster, Journey Guide progress and Coliseum readiness");
        return endpoints;
    }

    private static async Task<IResult> GetCurrentAsync(
        long allyCode,
        IEraService service,
        CancellationToken cancellationToken)
    {
        try
        {
            EraAnalysis? analysis = await service.GetCurrentAsync(allyCode, cancellationToken).ConfigureAwait(false);
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
