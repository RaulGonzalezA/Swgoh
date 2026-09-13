using Asp.Versioning;

using Swgoh.Application.Conquest;
using Swgoh.Domain.Conquest;

namespace Swgoh.Api.Endpoints;

internal static class ConquestEndpoints
{
    public static IEndpointRouteBuilder MapConquestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("Conquest");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/conquest/players/{allyCode:long}")
            .HasApiVersion(1.0)
            .WithTags("Conquest");

        group.MapGet("/current", GetCurrentAsync)
            .WithSummary("Get the current Conquest plan and feat progress");
        group.MapPut("/current", SaveCurrentAsync)
            .WithSummary("Save the current Conquest event, feats, stamina and data disks");
        group.MapPost("/current/optimize", OptimizeCurrentAsync)
            .WithSummary("Recommend stamina-aware teams and data disk loadouts for pending Conquest feats");

        return endpoints;
    }

    private static async Task<IResult> GetCurrentAsync(
        long allyCode,
        IConquestService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ConquestPlanDetails? plan = await service.GetCurrentAsync(allyCode, cancellationToken);
            return plan is null ? Results.NotFound() : Results.Ok(PlanResponse.From(plan));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> SaveCurrentAsync(
        long allyCode,
        SavePlanRequest request,
        IConquestService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            SaveConquestPlan input = new(
                request.EventId,
                request.Name,
                ParseDifficulty(request.Difficulty),
                [.. (request.Feats ?? []).Select(ToInput)],
                request.StaminaCostPerBattle ?? ConquestPlan.DefaultStaminaCostPerBattle,
                request.ReserveFloorPercent ?? ConquestPlan.DefaultReserveFloorPercent,
                [.. (request.Stamina ?? []).Select(value => new SaveConquestUnitStamina(
                    value.DefinitionId,
                    value.CurrentPercent))],
                request.DiskCapacityLimit ?? ConquestPlan.DefaultDiskCapacityLimit,
                [.. (request.DataDisks ?? []).Select(ToInput)],
                [.. (request.DiskLoadouts ?? []).Select(value => new SaveConquestDiskLoadout(
                    value.Id,
                    value.Name,
                    value.DiskIds ?? []))]);
            ConquestPlanDetails plan = await service.SaveAsync(allyCode, input, cancellationToken);
            return Results.Ok(PlanResponse.From(plan));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> OptimizeCurrentAsync(
        long allyCode,
        IConquestService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ConquestOptimizationResult? result = await service.OptimizeCurrentAsync(allyCode, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(OptimizationResponse.From(result));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static SaveConquestFeat ToInput(FeatRequest request) => new(
        request.Id,
        request.Name,
        ParseScope(request.Scope),
        request.Sector,
        request.Points,
        request.Target,
        request.Progress,
        request.ExpectedProgressPerBattle,
        ParseRuleType(request.RuleType),
        request.Faction,
        request.UnitDefinitionIds ?? [],
        request.MinimumMatchingUnits);

    private static SaveConquestDataDisk ToInput(DataDiskRequest request) => new(
        request.Id,
        request.Name,
        request.CapacityCost,
        request.PlannerBonus,
        ParseDiskTargetType(request.TargetType),
        request.Faction,
        request.UnitDefinitionIds ?? [],
        request.MinimumMatchingUnits,
        request.SupportedFeatIds ?? [],
        request.Notes);

    private static ConquestDifficulty ParseDifficulty(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Enum.TryParse(value.Trim(), ignoreCase: true, out ConquestDifficulty parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("Difficulty must be Easy, Normal or Hard.", nameof(value));
    }

    private static ConquestFeatScope ParseScope(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Enum.TryParse(value.Trim(), ignoreCase: true, out ConquestFeatScope parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("Scope must be Global, Sector or Boss.", nameof(value));
    }

    private static ConquestFeatRuleType ParseRuleType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Enum.TryParse(value.Trim(), ignoreCase: true, out ConquestFeatRuleType parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("RuleType must be AnyCharacter, Faction or SpecificUnits.", nameof(value));
    }

    private static ConquestDataDiskTargetType ParseDiskTargetType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Enum.TryParse(value.Trim(), ignoreCase: true, out ConquestDataDiskTargetType parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("TargetType must be AnyTeam, Faction or SpecificUnits.", nameof(value));
    }

    private static IResult Validation(ArgumentException exception) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["conquest"] = [exception.Message] });

    internal sealed record SavePlanRequest(
        string EventId,
        string Name,
        string Difficulty,
        IReadOnlyCollection<FeatRequest>? Feats,
        int? StaminaCostPerBattle = null,
        int? ReserveFloorPercent = null,
        IReadOnlyCollection<UnitStaminaRequest>? Stamina = null,
        int? DiskCapacityLimit = null,
        IReadOnlyCollection<DataDiskRequest>? DataDisks = null,
        IReadOnlyCollection<DiskLoadoutRequest>? DiskLoadouts = null);

    internal sealed record UnitStaminaRequest(string DefinitionId, int CurrentPercent);

    internal sealed record DataDiskRequest(
        Guid? Id,
        string Name,
        int CapacityCost,
        decimal PlannerBonus,
        string TargetType,
        string? Faction,
        IReadOnlyCollection<string>? UnitDefinitionIds,
        int MinimumMatchingUnits,
        IReadOnlyCollection<Guid>? SupportedFeatIds,
        string? Notes);

    internal sealed record DiskLoadoutRequest(
        Guid? Id,
        string Name,
        IReadOnlyCollection<Guid>? DiskIds);

    internal sealed record FeatRequest(
        Guid? Id,
        string Name,
        string Scope,
        int? Sector,
        int Points,
        int Target,
        int Progress,
        int ExpectedProgressPerBattle,
        string RuleType,
        string? Faction,
        IReadOnlyCollection<string>? UnitDefinitionIds,
        int MinimumMatchingUnits);

    internal sealed record PlanResponse(
        string Id,
        long AllyCode,
        string EventId,
        string Name,
        string Difficulty,
        IReadOnlyCollection<FeatResponse> Feats,
        int CompletedFeats,
        int TotalFeats,
        int EarnedFeatPoints,
        int AvailableFeatPoints,
        int StaminaCostPerBattle,
        int ReserveFloorPercent,
        IReadOnlyCollection<UnitStaminaResponse> Stamina,
        int DiskCapacityLimit,
        IReadOnlyCollection<DataDiskResponse> DataDisks,
        IReadOnlyCollection<DiskLoadoutResponse> DiskLoadouts,
        DateTimeOffset UpdatedAtUtc)
    {
        public static PlanResponse From(ConquestPlanDetails plan) => new(
            plan.Id,
            plan.AllyCode,
            plan.EventId,
            plan.Name,
            plan.Difficulty.ToString(),
            [.. plan.Feats.Select(FeatResponse.From)],
            plan.CompletedFeats,
            plan.TotalFeats,
            plan.EarnedFeatPoints,
            plan.AvailableFeatPoints,
            plan.StaminaCostPerBattle,
            plan.ReserveFloorPercent,
            [.. plan.Stamina.Select(UnitStaminaResponse.From)],
            plan.DiskCapacityLimit,
            [.. plan.DataDisks.Select(DataDiskResponse.From)],
            [.. plan.DiskLoadouts.Select(DiskLoadoutResponse.From)],
            plan.UpdatedAtUtc);
    }

    internal sealed record UnitStaminaResponse(string DefinitionId, int CurrentPercent)
    {
        public static UnitStaminaResponse From(ConquestUnitStamina value) => new(
            value.DefinitionId,
            value.CurrentPercent);
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

    internal sealed record DiskLoadoutResponse(
        Guid Id,
        string Name,
        IReadOnlyCollection<Guid> DiskIds)
    {
        public static DiskLoadoutResponse From(ConquestDiskLoadout loadout) => new(
            loadout.Id,
            loadout.Name,
            loadout.DiskIds);
    }

    internal sealed record FeatResponse(
        Guid Id,
        string Name,
        string Scope,
        int? Sector,
        int Points,
        int Target,
        int Progress,
        int Remaining,
        int ExpectedProgressPerBattle,
        string RuleType,
        string? Faction,
        IReadOnlyCollection<string> UnitDefinitionIds,
        int MinimumMatchingUnits,
        bool IsComplete)
    {
        public static FeatResponse From(ConquestFeatDetails feat) => new(
            feat.Id,
            feat.Name,
            feat.Scope.ToString(),
            feat.Sector,
            feat.Points,
            feat.Target,
            feat.Progress,
            feat.Remaining,
            feat.ExpectedProgressPerBattle,
            feat.Rule.Type.ToString(),
            feat.Rule.Faction,
            feat.Rule.UnitDefinitionIds,
            feat.Rule.MinimumMatchingUnits,
            feat.IsComplete);
    }

    internal sealed record OptimizationResponse(
        long AllyCode,
        string EventId,
        int PendingFeats,
        int CandidateCharacters,
        int StaminaCostPerBattle,
        int ReserveFloorPercent,
        int DiskCapacityLimit,
        IReadOnlyCollection<TeamResponse> Recommendations,
        IReadOnlyCollection<Guid> UncoveredFeatIds)
    {
        public static OptimizationResponse From(ConquestOptimizationResult result) => new(
            result.AllyCode,
            result.EventId,
            result.PendingFeats,
            result.CandidateCharacters,
            result.StaminaCostPerBattle,
            result.ReserveFloorPercent,
            result.DiskCapacityLimit,
            [.. result.Recommendations.Select(TeamResponse.From)],
            result.UncoveredFeatIds);
    }

    internal sealed record TeamResponse(
        int Rank,
        decimal Score,
        decimal FeatEfficiency,
        long TeamGalacticPower,
        decimal? AverageSpeed,
        decimal AverageStamina,
        decimal ExpectedPostBattleAverageStamina,
        decimal StaminaOpportunityCost,
        int ReserveRiskUnits,
        DiskRecommendationResponse? DiskLoadout,
        IReadOnlyCollection<UnitResponse> Team,
        IReadOnlyCollection<ContributionResponse> AdvancesFeats,
        string Rationale)
    {
        public static TeamResponse From(ConquestTeamRecommendation recommendation) => new(
            recommendation.Rank,
            recommendation.Score,
            recommendation.FeatEfficiency,
            recommendation.TeamGalacticPower,
            recommendation.AverageSpeed,
            recommendation.AverageStamina,
            recommendation.ExpectedPostBattleAverageStamina,
            recommendation.StaminaOpportunityCost,
            recommendation.ReserveRiskUnits,
            recommendation.DiskLoadout is null ? null : DiskRecommendationResponse.From(recommendation.DiskLoadout),
            [.. recommendation.Team.Select(UnitResponse.From)],
            [.. recommendation.AdvancesFeats.Select(ContributionResponse.From)],
            recommendation.Rationale);
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
        public static DiskRecommendationResponse From(ConquestDiskRecommendation recommendation) => new(
            recommendation.LoadoutId,
            recommendation.LoadoutName,
            recommendation.CapacityUsed,
            recommendation.CapacityLimit,
            recommendation.PlannerBonus,
            [.. recommendation.Disks.Select(DataDiskResponse.From)],
            recommendation.MatchedFeatIds);
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

    internal sealed record ContributionResponse(
        Guid FeatId,
        string FeatName,
        int Points,
        int Remaining,
        int ExpectedProgress,
        decimal PointValueThisBattle)
    {
        public static ContributionResponse From(ConquestFeatContribution contribution) => new(
            contribution.FeatId,
            contribution.FeatName,
            contribution.Points,
            contribution.Remaining,
            contribution.ExpectedProgress,
            contribution.PointValueThisBattle);
    }
}
