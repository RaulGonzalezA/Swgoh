using Asp.Versioning;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Api.Endpoints;

internal static class GacAttackExecutionEndpoints
{
    public static IEndpointRouteBuilder MapGacAttackExecutionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("GAC Attack Execution");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/gac/players/{allyCode:long}/planner/current/attacks")
            .HasApiVersion(1.0)
            .WithTags("GAC Planner");

        group.MapPost("/{attackId:guid}/result", ExecuteAsync)
            .WithSummary("Record a GAC attack result, remaining enemies and preload state, then recalculate the next attack");

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(
        long allyCode,
        Guid attackId,
        ExecuteAttackRequest request,
        IGacAttackExecutionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            GacAttackExecutionLookup lookup = await service.ExecuteAsync(
                allyCode,
                attackId,
                new ExecuteGacAttackResult(
                    ParseStatus(request.Status),
                    request.Banners,
                    request.Notes,
                    request.RemainingEnemyUnitDefinitionIds,
                    request.PreloadedTurnMeter),
                cancellationToken);
            return ToResult(lookup);
        }
        catch (GacPlannerConcurrencyException exception)
        {
            return Results.Conflict(
                new GacPlannerEndpoints.PlannerUnavailableResponse("Conflict", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["execution"] = [exception.Message] });
        }
    }

    private static IResult ToResult(GacAttackExecutionLookup lookup)
    {
        if (lookup.IsAvailable && lookup.Execution is not null)
        {
            GacAttackExecutionResult execution = lookup.Execution;
            return Results.Ok(new ExecutionEnvelope(
                GacPlannerEndpoints.GacPlannerResponse.From(execution.State),
                execution.Optimization is null
                    ? null
                    : GacPlannerOptimizationEndpoints.OptimizationResponse.From(execution.Optimization),
                new ExecutedAttackResponse(
                    execution.AttackId,
                    execution.Status.ToString(),
                    execution.Banners,
                    execution.Notes,
                    execution.RemainingEnemyUnitDefinitionIds,
                    execution.PreloadedTurnMeter,
                    execution.IsCleanup),
                execution.NextRecommendation is null
                    ? null
                    : GacPlannerOptimizationEndpoints.OptimizationRecommendationResponse.From(
                        execution.NextRecommendation),
                execution.Replan is null ? null : WarRoomReplanResponse.From(execution.Replan),
                execution.PostCommitWarnings));
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

    private static GacAttackPlanStatus ParseStatus(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant() switch
        {
            "won" => GacAttackPlanStatus.Won,
            "failed" => GacAttackPlanStatus.Failed,
            _ => throw new ArgumentException("Execution status must be Won or Failed.", nameof(value))
        };
    }

    internal sealed record ExecuteAttackRequest(
        string Status,
        int? Banners,
        string? Notes,
        IReadOnlyCollection<string>? RemainingEnemyUnitDefinitionIds = null,
        bool PreloadedTurnMeter = false);

    internal sealed record ExecutionEnvelope(
        GacPlannerEndpoints.GacPlannerResponse Planner,
        GacPlannerOptimizationEndpoints.OptimizationResponse? Optimization,
        ExecutedAttackResponse Execution,
        GacPlannerOptimizationEndpoints.OptimizationRecommendationResponse? NextRecommendation,
        WarRoomReplanResponse? Replan,
        IReadOnlyCollection<string> Warnings);

    internal sealed record WarRoomReplanResponse(
        bool Applied,
        int PreviousPendingAttacks,
        int CurrentPendingAttacks,
        int ReplacedPendingAttacks,
        int CoveredDefenses,
        int UncoveredDefenses,
        IReadOnlyCollection<Guid> ReplannedDefenseIds)
    {
        public static WarRoomReplanResponse From(GacWarRoomReplanSummary summary) => new(
            summary.Applied,
            summary.PreviousPendingAttacks,
            summary.CurrentPendingAttacks,
            summary.ReplacedPendingAttacks,
            summary.CoveredDefenses,
            summary.UncoveredDefenses,
            summary.ReplannedDefenseIds);
    }

    internal sealed record ExecutedAttackResponse(
        Guid AttackId,
        string Status,
        int? Banners,
        string? Notes,
        IReadOnlyCollection<string> RemainingEnemyUnitDefinitionIds,
        bool PreloadedTurnMeter,
        bool IsCleanup);
}
