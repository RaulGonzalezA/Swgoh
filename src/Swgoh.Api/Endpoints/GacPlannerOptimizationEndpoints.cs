using Asp.Versioning;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Api.Endpoints;

internal static class GacPlannerOptimizationEndpoints
{
    public static IEndpointRouteBuilder MapGacPlannerOptimizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("GAC Planner Optimization");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/gac/players/{allyCode:long}/planner")
            .HasApiVersion(1.0)
            .WithTags("GAC Planner");

        group.MapPost("/current/optimize", OptimizeCurrentAsync)
            .WithSummary("Preview or apply an optimized attack plan for the current GAC round");

        return endpoints;
    }

    private static async Task<IResult> OptimizeCurrentAsync(
        long allyCode,
        OptimizeRequest request,
        IGacAttackPlanOptimizerService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            GacAttackOptimizationLookup lookup = await service.OptimizeCurrentAsync(
                allyCode,
                ParseMode(request.Mode),
                request.Apply,
                cancellationToken);
            return ToResult(lookup);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["optimizer"] = [exception.Message] });
        }
    }

    private static IResult ToResult(GacAttackOptimizationLookup lookup)
    {
        if (lookup.IsAvailable && lookup.State is not null && lookup.Optimization is not null)
        {
            return Results.Ok(new OptimizationEnvelope(
                GacPlannerEndpoints.GacPlannerResponse.From(lookup.State),
                OptimizationResponse.From(lookup.Optimization)));
        }

        var unavailable = new GacPlannerEndpoints.PlannerUnavailableResponse(
            lookup.Status.ToString(),
            lookup.Message);
        return lookup.Status switch
        {
            CurrentGacOpponentStatus.NoActiveEvent or CurrentGacOpponentStatus.PlayerNotJoined =>
                Results.NotFound(unavailable),
            CurrentGacOpponentStatus.OpponentUnavailable or CurrentGacOpponentStatus.FormatUnavailable =>
                Results.Conflict(unavailable),
            _ => Results.Problem(statusCode: StatusCodes.Status502BadGateway)
        };
    }

    private static GacAttackOptimizationMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GacAttackOptimizationMode.FillGaps;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out GacAttackOptimizationMode mode) && Enum.IsDefined(mode)
            ? mode
            : throw new ArgumentException("Optimization mode must be FillGaps or RebuildPlanned.", nameof(value));
    }

    internal sealed record OptimizeRequest(string? Mode, bool Apply = false);

    internal sealed record OptimizationEnvelope(
        GacPlannerEndpoints.GacPlannerResponse Planner,
        OptimizationResponse Optimization);

    internal sealed record OptimizationResponse(
        string Mode,
        bool Applied,
        int TargetDefenses,
        int RecommendedAttacks,
        int HistoricalMatches,
        decimal AverageScore,
        decimal? KnownAverageBanners,
        IReadOnlyCollection<Guid> UncoveredDefenseIds,
        IReadOnlyCollection<OptimizationRecommendationResponse> Recommendations,
        bool SearchLimitReached)
    {
        public static OptimizationResponse From(GacAttackOptimizationResult result) => new(
            result.Mode.ToString(),
            result.Applied,
            result.TargetDefenses,
            result.RecommendedAttacks,
            result.HistoricalMatches,
            result.AverageScore,
            result.KnownAverageBanners,
            result.UncoveredDefenseIds,
            [.. result.Recommendations.Select(OptimizationRecommendationResponse.From)],
            result.SearchLimitReached);
    }

    internal sealed record OptimizationRecommendationResponse(
        Guid DefenseId,
        string DefenseName,
        string Zone,
        Guid TeamPresetId,
        string TeamName,
        decimal Score,
        decimal StrategicCost,
        string Evidence,
        string Confidence,
        string Rationale,
        decimal? WinRate,
        decimal? OneShotRate,
        decimal? AverageBanners,
        int? Uses,
        decimal TacticalAdjustment,
        decimal? TeamAverageSpeed,
        decimal? DefenseAverageSpeed,
        decimal? TeamModSpeedBonus,
        decimal? DefenseModSpeedBonus,
        string DatacronStatus)
    {
        public static OptimizationRecommendationResponse From(GacAttackOptimizationRecommendation recommendation) => new(
            recommendation.DefenseId,
            recommendation.DefenseName,
            recommendation.Zone,
            recommendation.TeamPresetId,
            recommendation.TeamName,
            recommendation.Score,
            recommendation.StrategicCost,
            recommendation.Evidence,
            recommendation.Confidence,
            recommendation.Rationale,
            recommendation.WinRate,
            recommendation.OneShotRate,
            recommendation.AverageBanners,
            recommendation.Uses,
            recommendation.TacticalAdjustment,
            recommendation.TeamAverageSpeed,
            recommendation.DefenseAverageSpeed,
            recommendation.TeamModSpeedBonus,
            recommendation.DefenseModSpeedBonus,
            recommendation.DatacronStatus);
    }
}
