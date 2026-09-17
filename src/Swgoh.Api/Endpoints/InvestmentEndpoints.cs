using Asp.Versioning;

using Swgoh.Application.Investments;

namespace Swgoh.Api.Endpoints;

internal static class InvestmentEndpoints
{
    public static IEndpointRouteBuilder MapInvestmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("Investments");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/investments/players/{allyCode:long}")
            .HasApiVersion(1.0)
            .WithTags("Investments");

        group.MapGet("/current", GetCurrentAsync)
            .WithSummary("Build a single investment priority list across GAC, RotE, Conquest, Era and Coliseum");
        return endpoints;
    }

    private static async Task<IResult> GetCurrentAsync(
        long allyCode,
        IInvestmentOptimizerService service,
        CancellationToken cancellationToken)
    {
        try
        {
            InvestmentOptimizationResult? result = await service
                .GetCurrentAsync(allyCode, cancellationToken)
                .ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
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
