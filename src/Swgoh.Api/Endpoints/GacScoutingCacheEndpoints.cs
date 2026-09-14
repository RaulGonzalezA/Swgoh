using Asp.Versioning;

using Swgoh.Application.Gac;

namespace Swgoh.Api.Endpoints;

internal static class GacScoutingCacheEndpoints
{
    public static IEndpointRouteBuilder MapGacScoutingCacheEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("GAC scouting cache");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/gac")
            .HasApiVersion(1.0)
            .WithTags("GAC");

        group.MapPost(
                "/players/{allyCode:long}/current-opponent/scouting/refresh",
                RefreshScouting)
            .WithSummary("Invalidate cached scouting for the active GAC matchup");

        return endpoints;
    }

    private static IResult RefreshScouting(long allyCode, ICurrentGacScoutingCache cache)
    {
        if (allyCode is < 100_000_000 or > 999_999_999)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["allyCode"] = ["Ally code must contain exactly nine digits."]
            });
        }

        cache.Invalidate(allyCode);
        return Results.NoContent();
    }
}
