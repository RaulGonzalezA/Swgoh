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
            .WithSummary("Preview or apply an optimized attack plan with Counter Engine 2.0 analysis")
            .RequireRateLimiting("gac-optimizer")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("/current/optimize-round", OptimizeRoundAsync)
            .WithSummary("Preview or apply a joint defense and attack optimization for the current GAC round")
            .RequireRateLimiting("gac-optimizer")
            .Produces(StatusCodes.Status429TooManyRequests);

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
        catch (GacPlannerConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["optimizer"] = [exception.Message] });
        }
    }

    private static async Task<IResult> OptimizeRoundAsync(
        long allyCode,
        JointOptimizeRequest request,
        IGacJointRoundOptimizerService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            GacJointRoundOptimizationLookup lookup = await service.OptimizeCurrentAsync(
                allyCode,
                ParseJointMode(request.Mode),
                request.Apply,
                cancellationToken);
            return ToJointResult(lookup);
        }
        catch (GacPlannerConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["optimizer"] = [exception.Message] });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }

    private static IResult Conflict(GacPlannerConcurrencyException exception) =>
        Results.Conflict(new GacPlannerEndpoints.PlannerUnavailableResponse("Conflict", exception.Message));

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

    private static IResult ToJointResult(GacJointRoundOptimizationLookup lookup)
    {
        if (lookup.IsAvailable && lookup.State is not null && lookup.Optimization is not null)
        {
            return Results.Ok(new JointOptimizationEnvelope(
                GacPlannerEndpoints.GacPlannerResponse.From(lookup.State),
                JointOptimizationResponse.From(lookup.Optimization)));
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

    private static GacJointRoundOptimizationMode ParseJointMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GacJointRoundOptimizationMode.Balanced;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out GacJointRoundOptimizationMode mode) && Enum.IsDefined(mode)
            ? mode
            : throw new ArgumentException(
                "Joint optimization mode must be Balanced, DefenseFirst, OffenseFirst or MaxBanners.",
                nameof(value));
    }

    internal sealed record OptimizeRequest(string? Mode, bool Apply = false);
    internal sealed record JointOptimizeRequest(string? Mode, bool Apply = false);

    internal sealed record OptimizationEnvelope(
        GacPlannerEndpoints.GacPlannerResponse Planner,
        OptimizationResponse Optimization);

    internal sealed record JointOptimizationEnvelope(
        GacPlannerEndpoints.GacPlannerResponse Planner,
        JointOptimizationResponse Optimization);

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
        bool SearchLimitReached,
        IReadOnlyCollection<CounterDefenseAnalysisResponse> CounterAnalyses)
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
            result.SearchLimitReached,
            [.. result.CounterEngine.Select(CounterDefenseAnalysisResponse.From)]);
    }

    internal sealed record CounterDefenseAnalysisResponse(
        Guid DefenseId,
        string DefenseName,
        string Zone,
        IReadOnlyCollection<CounterCandidateAnalysisResponse> Candidates)
    {
        public static CounterDefenseAnalysisResponse From(GacCounterDefenseAnalysis analysis) => new(
            analysis.DefenseId,
            analysis.DefenseName,
            analysis.Zone,
            [.. analysis.Candidates.Select(CounterCandidateAnalysisResponse.From)]);
    }

    internal sealed record CounterCandidateAnalysisResponse(
        int Rank,
        Guid TeamPresetId,
        string TeamName,
        decimal Score,
        decimal EstimatedWinProbability,
        decimal? ExpectedBanners,
        string Risk,
        string TimeoutRisk,
        decimal StrategicCost,
        decimal CriticalPieceCost,
        string Evidence,
        string Confidence,
        string Rationale,
        string DatacronStatus,
        decimal TacticalAdjustment,
        decimal PersonalAdjustment,
        int FutureDefensesAtRisk)
    {
        public static CounterCandidateAnalysisResponse From(GacCounterCandidateAnalysis candidate) => new(
            candidate.Rank,
            candidate.TeamPresetId,
            candidate.TeamName,
            candidate.Score,
            candidate.EstimatedWinProbability,
            candidate.ExpectedBanners,
            candidate.Risk,
            candidate.TimeoutRisk,
            candidate.StrategicCost,
            candidate.CriticalPieceCost,
            candidate.Evidence,
            candidate.Confidence,
            candidate.Rationale,
            candidate.DatacronStatus,
            candidate.TacticalAdjustment,
            candidate.PersonalAdjustment,
            candidate.FutureDefensesAtRisk);
    }

    internal sealed record JointOptimizationResponse(
        string Format,
        string Mode,
        bool Applied,
        int ScenariosEvaluated,
        JointScenarioResponse Selected,
        IReadOnlyCollection<JointScenarioResponse> Alternatives,
        DateTimeOffset? PlanUpdatedAtUtc,
        IReadOnlyCollection<string> Warnings)
    {
        public static JointOptimizationResponse From(GacJointRoundOptimizationResult result) => new(
            result.Format == GacFormat.ThreeVsThree ? "3v3" : "5v5",
            result.Mode.ToString(),
            result.Applied,
            result.ScenariosEvaluated,
            JointScenarioResponse.From(result.Selected),
            [.. result.Alternatives.Select(JointScenarioResponse.From)],
            result.PlanUpdatedAtUtc,
            result.Warnings);
    }

    internal sealed record JointScenarioResponse(
        string ScenarioId,
        decimal JointScore,
        decimal DefenseScore,
        decimal DefenseCompletionRate,
        decimal AttackCoverageRate,
        decimal AttackScore,
        decimal? KnownAverageBanners,
        decimal OffensePreservationScore,
        decimal AverageDefenseOpportunityCost,
        int RecommendedAttacks,
        int TargetDefenses,
        int HistoricalMatches,
        bool AttackSearchLimitReached,
        IReadOnlyCollection<JointDefenseAssignmentResponse> DefenseAssignments,
        IReadOnlyCollection<OptimizationRecommendationResponse> AttackRecommendations,
        IReadOnlyCollection<Guid> UncoveredDefenseIds,
        IReadOnlyCollection<string> Warnings)
    {
        public static JointScenarioResponse From(GacJointRoundScenario scenario) => new(
            scenario.ScenarioId,
            scenario.JointScore,
            scenario.DefenseScore,
            scenario.DefenseCompletionRate,
            scenario.AttackCoverageRate,
            scenario.AttackScore,
            scenario.KnownAverageBanners,
            scenario.OffensePreservationScore,
            scenario.AverageDefenseOpportunityCost,
            scenario.RecommendedAttacks,
            scenario.TargetDefenses,
            scenario.HistoricalMatches,
            scenario.AttackSearchLimitReached,
            [.. scenario.DefenseAssignments.Select(JointDefenseAssignmentResponse.From)],
            [.. scenario.AttackRecommendations.Select(OptimizationRecommendationResponse.From)],
            scenario.UncoveredDefenseIds,
            scenario.Warnings);
    }

    internal sealed record JointDefenseAssignmentResponse(
        int Position,
        string Zone,
        Guid TeamPresetId,
        string TeamName,
        bool Pinned,
        bool IsFleet,
        long GalacticPower,
        decimal Score,
        decimal DefensiveValue,
        decimal OffensiveOpportunityCost,
        string Confidence,
        bool ContainsGalacticLegend,
        int OmicronCount,
        int EligibleDatacronTier,
        int OpponentSamples,
        int PersonalSamples,
        IReadOnlyCollection<string> Reasons)
    {
        public static JointDefenseAssignmentResponse From(GacSmartDefenseAssignment item) => new(
            item.Position,
            item.Zone,
            item.TeamPresetId,
            item.TeamName,
            item.Pinned,
            item.IsFleet,
            item.GalacticPower,
            item.Score,
            item.DefensiveValue,
            item.OffensiveOpportunityCost,
            item.Confidence,
            item.ContainsGalacticLegend,
            item.OmicronCount,
            item.EligibleDatacronTier,
            item.OpponentSamples,
            item.PersonalSamples,
            item.Reasons);
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
        string DatacronStatus,
        decimal BaseStrategicCost,
        decimal OpportunityCost,
        int StrategicAlternatives,
        int FutureDefensesAtRisk,
        string StrategicRationale,
        decimal PersonalAdjustment,
        int PersonalSamples,
        int PersonalWins,
        decimal? PersonalWinRate,
        decimal? PersonalOneShotRate,
        decimal? PersonalAverageBanners,
        string PersonalScope,
        string PersonalRationale,
        decimal EstimatedWinProbability,
        string Risk,
        string TimeoutRisk,
        decimal CriticalPieceCost)
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
            recommendation.DatacronStatus,
            recommendation.BaseStrategicCost,
            recommendation.OpportunityCost,
            recommendation.StrategicAlternatives,
            recommendation.FutureDefensesAtRisk,
            recommendation.StrategicRationale,
            recommendation.PersonalAdjustment,
            recommendation.PersonalSamples,
            recommendation.PersonalWins,
            recommendation.PersonalWinRate,
            recommendation.PersonalOneShotRate,
            recommendation.PersonalAverageBanners,
            recommendation.PersonalScope,
            recommendation.PersonalRationale,
            recommendation.EstimatedWinProbability,
            recommendation.Risk,
            recommendation.TimeoutRisk,
            recommendation.CriticalPieceCost);
    }
}
