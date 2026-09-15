using Asp.Versioning;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Api.Endpoints;

internal static class GacGeneratedDefenseCleanupEndpoints
{
    public static IEndpointRouteBuilder MapGacGeneratedDefenseCleanupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("GAC Generated Defense Cleanup");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/gac/players/{allyCode:long}/planner/strategy")
            .HasApiVersion(1.0)
            .WithTags("GAC Planner");

        group.MapDelete("/generated-defense-presets", DeleteAsync)
            .WithSummary("Delete automatically generated defense presets and remove their current defense assignments");

        return endpoints;
    }

    private static async Task<IResult> DeleteAsync(
        long allyCode,
        string format,
        IGacGeneratedDefenseCleanupService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacGeneratedDefenseCleanupResult result = await service
                .DeleteAsync(allyCode, ParseFormat(format), cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(CleanupResponse.From(result));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["cleanup"] = [exception.Message] });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }

    private static GacFormat ParseFormat(string value) => value.Trim().ToLowerInvariant() switch
    {
        "3" or "3v3" => GacFormat.ThreeVsThree,
        "5" or "5v5" => GacFormat.FiveVsFive,
        _ => throw new ArgumentException("Format must be 3v3 or 5v5.", nameof(value))
    };

    private static string FormatName(GacFormat format) => format switch
    {
        GacFormat.ThreeVsThree => "3v3",
        GacFormat.FiveVsFive => "5v5",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.")
    };

    internal sealed record CleanupResponse(
        string Format,
        int DeletedPresets,
        int RemovedDefenseAssignments,
        IReadOnlyCollection<string> DeletedTeamNames,
        IReadOnlyCollection<string> Warnings)
    {
        public static CleanupResponse From(GacGeneratedDefenseCleanupResult result) => new(
            FormatName(result.Format),
            result.DeletedPresets,
            result.RemovedDefenseAssignments,
            result.DeletedTeamNames,
            result.Warnings);
    }
}
