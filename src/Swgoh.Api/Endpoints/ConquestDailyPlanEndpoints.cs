using Asp.Versioning;

using Swgoh.Application.Conquest;
using Swgoh.Domain.Conquest;

namespace Swgoh.Api.Endpoints;

internal static class ConquestDailyPlanEndpoints
{
    public static IEndpointRouteBuilder MapConquestDailyPlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("ConquestDailyPlan");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/conquest/players/{allyCode:long}/current")
            .HasApiVersion(1.0)
            .WithTags("Conquest");

        group.MapPost("/daily-plan", BuildDailyPlanAsync)
            .WithSummary("Build a sequential Conquest battle plan using projected feat progress, stamina and data disks");

        return endpoints;
    }

    private static async Task<IResult> BuildDailyPlanAsync(
        long allyCode,
        DailyPlanRequest request,
        IConquestDailyPlanService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ConquestDailyPlanResult? result = await service.BuildAsync(
                allyCode,
                new ConquestDailyPlanRequest(request.MaxBattles),
                cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(DailyPlanResponse.From(result));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["conquest"] = [exception.Message] });
        }
    }

    internal sealed record DailyPlanRequest(int MaxBattles = 6);

    internal sealed record DailyPlanResponse(
        long AllyCode,
        string EventId,
        int RequestedBattles,
        int PlannedBattles,
        int StartingPendingFeats,
        int ProjectedCompletedFeats,
        int ProjectedRemainingFeats,
        string StopReason,
        IReadOnlyCollection<DailyPlanStepResponse> Steps,
        IReadOnlyCollection<RecoveryUnitResponse> RecoveryPriority,
        IReadOnlyCollection<Guid> RemainingFeatIds)
    {
        public static DailyPlanResponse From(ConquestDailyPlanResult result) => new(
            result.AllyCode,
            result.EventId,
            result.RequestedBattles,
            result.PlannedBattles,
            result.StartingPendingFeats,
            result.ProjectedCompletedFeats,
            result.ProjectedRemainingFeats,
            result.StopReason,
            [.. result.Steps.Select(DailyPlanStepResponse.From)],
            [.. result.RecoveryPriority.Select(RecoveryUnitResponse.From)],
            result.RemainingFeatIds);
    }

    internal sealed record DailyPlanStepResponse(
        int BattleNumber,
        decimal Score,
        decimal FeatEfficiency,
        decimal AverageStaminaBefore,
        decimal AverageStaminaAfter,
        int ReserveRiskUnits,
        bool ChangesTeamFromPrevious,
        bool ChangesLoadoutFromPrevious,
        DiskRecommendationResponse? DiskLoadout,
        IReadOnlyCollection<UnitResponse> Team,
        IReadOnlyCollection<FeatProgressResponse> FeatProgress,
        string Rationale)
    {
        public static DailyPlanStepResponse From(ConquestDailyPlanStep step) => new(
            step.BattleNumber,
            step.Score,
            step.FeatEfficiency,
            step.AverageStaminaBefore,
            step.AverageStaminaAfter,
            step.ReserveRiskUnits,
            step.ChangesTeamFromPrevious,
            step.ChangesLoadoutFromPrevious,
            step.DiskLoadout is null ? null : DiskRecommendationResponse.From(step.DiskLoadout),
            [.. step.Team.Select(UnitResponse.From)],
            [.. step.FeatProgress.Select(FeatProgressResponse.From)],
            step.Rationale);
    }

    internal sealed record FeatProgressResponse(
        Guid FeatId,
        string FeatName,
        int Points,
        int BeforeProgress,
        int AfterProgress,
        int Target,
        bool CompletedByBattle)
    {
        public static FeatProgressResponse From(ConquestDailyFeatProgress value) => new(
            value.FeatId,
            value.FeatName,
            value.Points,
            value.BeforeProgress,
            value.AfterProgress,
            value.Target,
            value.CompletedByBattle);
    }

    internal sealed record RecoveryUnitResponse(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        int FinalStamina,
        int ReserveFloorPercent)
    {
        public static RecoveryUnitResponse From(ConquestDailyRecoveryUnit value) => new(
            value.DefinitionId,
            value.Name,
            value.ThumbnailName,
            value.FinalStamina,
            value.ReserveFloorPercent);
    }

    internal sealed record DiskRecommendationResponse(
        Guid LoadoutId,
        string LoadoutName,
        int CapacityUsed,
        int CapacityLimit,
        decimal PlannerBonus,
        IReadOnlyCollection<DataDiskResponse> Disks,
        IReadOnlyCollection<Guid> MatchedFeatIds)
    {
        public static DiskRecommendationResponse From(ConquestDiskRecommendation value) => new(
            value.LoadoutId,
            value.LoadoutName,
            value.CapacityUsed,
            value.CapacityLimit,
            value.PlannerBonus,
            [.. value.Disks.Select(DataDiskResponse.From)],
            value.MatchedFeatIds);
    }

    internal sealed record DataDiskResponse(
        Guid Id,
        string Name,
        int CapacityCost,
        decimal PlannerBonus,
        string TargetType,
        string? Faction,
        IReadOnlyCollection<string> UnitDefinitionIds,
        int MinimumMatchingUnits,
        IReadOnlyCollection<Guid> SupportedFeatIds,
        string? Notes)
    {
        public static DataDiskResponse From(ConquestDataDisk disk) => new(
            disk.Id,
            disk.Name,
            disk.CapacityCost,
            disk.PlannerBonus,
            disk.Target.Type.ToString(),
            disk.Target.Faction,
            disk.Target.UnitDefinitionIds,
            disk.Target.MinimumMatchingUnits,
            disk.SupportedFeatIds,
            disk.Notes);
    }

    internal sealed record UnitResponse(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        int RelicTier,
        long GalacticPower,
        decimal? Speed,
        IReadOnlyCollection<string> Factions,
        int CurrentStamina,
        int ExpectedPostBattleStamina,
        bool BelowReserveAfterBattle)
    {
        public static UnitResponse From(ConquestOptimizationUnit unit) => new(
            unit.DefinitionId,
            unit.Name,
            unit.ThumbnailName,
            unit.RelicTier,
            unit.GalacticPower,
            unit.Speed,
            unit.Factions,
            unit.CurrentStamina,
            unit.ExpectedPostBattleStamina,
            unit.BelowReserveAfterBattle);
    }
}
