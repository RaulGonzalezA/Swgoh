using Asp.Versioning;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Api.Endpoints;

internal static class GacDefenseStrategyEndpoints
{
    public static IEndpointRouteBuilder MapGacDefenseStrategyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("GAC Defense Strategy");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/gac/players/{allyCode:long}/planner/strategy")
            .HasApiVersion(1.0)
            .WithTags("GAC Planner");

        group.MapGet("", GetAsync)
            .WithSummary("Get the reusable GAC defense template and attack reservations for a format");
        group.MapPut("", SaveAsync)
            .WithSummary("Save the reusable GAC defense template and attack reservations for a format");
        group.MapPost("/generate-defense", GenerateDefenseAsync)
            .WithSummary("Preview or apply an automatically generated defense for the current GAC round");

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        long allyCode,
        string format,
        IGacDefenseStrategyService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacDefenseStrategySnapshot snapshot = await service
                .GetAsync(allyCode, ParseFormat(format), cancellationToken);
            return Results.Ok(StrategyResponse.From(snapshot));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> SaveAsync(
        long allyCode,
        SaveStrategyRequest request,
        IGacDefenseStrategyService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            GacDefenseStrategySnapshot snapshot = await service.SaveAsync(
                allyCode,
                new SaveGacDefenseStrategy(
                    ParseFormat(request.Format),
                    [.. (request.Slots ?? []).Select(item => new GacDefenseTemplateSlot(
                        item.Position,
                        item.Zone,
                        item.PinnedTeamPresetId))],
                    request.ReservedAttackPresetIds ?? []),
                cancellationToken);
            return Results.Ok(StrategyResponse.From(snapshot));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GenerateDefenseAsync(
        long allyCode,
        GenerateDefenseRequest request,
        IGacDefenseStrategyService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacDefenseGenerationResult result = await service
                .GenerateCurrentAsync(allyCode, request.Apply, cancellationToken);
            return Results.Ok(GenerationResponse.From(result));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
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

    private static IResult Validation(ArgumentException exception) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["strategy"] = [exception.Message] });

    internal sealed record SaveStrategyRequest(
        string Format,
        IReadOnlyCollection<StrategySlotRequest>? Slots,
        IReadOnlyCollection<Guid>? ReservedAttackPresetIds);

    internal sealed record StrategySlotRequest(
        int Position,
        string Zone,
        Guid? PinnedTeamPresetId);

    internal sealed record GenerateDefenseRequest(bool Apply);

    internal sealed record StrategyResponse(
        string Format,
        IReadOnlyCollection<StrategySlotResponse> Slots,
        IReadOnlyCollection<Guid> ReservedAttackPresetIds,
        IReadOnlyCollection<StrategyPresetResponse> Presets,
        DateTimeOffset UpdatedAtUtc)
    {
        public static StrategyResponse From(GacDefenseStrategySnapshot snapshot) => new(
            FormatName(snapshot.Profile.Format),
            [.. snapshot.Profile.Slots.Select(item => new StrategySlotResponse(
                item.Position,
                item.Zone,
                item.PinnedTeamPresetId))],
            snapshot.Profile.ReservedAttackPresetIds,
            [.. snapshot.Presets.Select(item => new StrategyPresetResponse(
                item.Id,
                item.Name,
                item.Use.ToString(),
                item.IsFleet))],
            snapshot.Profile.UpdatedAtUtc);
    }

    internal sealed record StrategySlotResponse(
        int Position,
        string Zone,
        Guid? PinnedTeamPresetId);

    internal sealed record StrategyPresetResponse(
        Guid Id,
        string Name,
        string Use,
        bool IsFleet);

    internal sealed record GenerationResponse(
        string Format,
        bool Applied,
        IReadOnlyCollection<GeneratedAssignmentResponse> Assignments,
        IReadOnlyCollection<string> Warnings,
        DateTimeOffset? PlanUpdatedAtUtc)
    {
        public static GenerationResponse From(GacDefenseGenerationResult result) => new(
            FormatName(result.Format),
            result.Applied,
            [.. result.Assignments.Select(item => new GeneratedAssignmentResponse(
                item.Position,
                item.Zone,
                item.TeamPresetId,
                item.TeamName,
                item.Pinned,
                item.IsFleet,
                item.GalacticPower))],
            result.Warnings,
            result.PlanUpdatedAtUtc);
    }

    internal sealed record GeneratedAssignmentResponse(
        int Position,
        string Zone,
        Guid TeamPresetId,
        string TeamName,
        bool Pinned,
        bool IsFleet,
        long GalacticPower);
}
