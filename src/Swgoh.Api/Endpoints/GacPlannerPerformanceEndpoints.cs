using Asp.Versioning;

using Swgoh.Application.Gac;
using Swgoh.Application.Players;
using Swgoh.Domain.Gac;

namespace Swgoh.Api.Endpoints;

internal static class GacPlannerPerformanceEndpoints
{
    public static IEndpointRouteBuilder MapGacPlannerPerformanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("GAC Planner Performance");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/gac/players/{allyCode:long}/planner")
            .HasApiVersion(1.0)
            .WithTags("GAC Planner");

        group.MapGet("/context", GetContextAsync)
            .WithSummary("Get the current GAC planner and both enriched rosters in one request");
        group.MapPost("/current/own-defenses", AddOwnDefenseAsync)
            .WithSummary("Add one team to the player's current GAC defense");
        group.MapDelete("/current/own-defenses/{assignmentId:guid}", RemoveOwnDefenseAsync)
            .WithSummary("Remove one team from the player's current GAC defense");
        group.MapPost("/current/visible-defenses", AddVisibleDefenseAsync)
            .WithSummary("Add one visible opponent defense to the current GAC plan");
        group.MapDelete("/current/visible-defenses/{defenseId:guid}", RemoveVisibleDefenseAsync)
            .WithSummary("Remove one visible opponent defense and its attacks from the current GAC plan");
        group.MapPost("/current/attacks", AddAttackAsync)
            .WithSummary("Plan one attack against a visible GAC defense");
        group.MapPatch("/current/attacks/{attackId:guid}", UpdateAttackAsync)
            .WithSummary("Update the status or notes of one planned GAC attack");

        return endpoints;
    }

    private static async Task<IResult> GetContextAsync(
        long allyCode,
        IGacPlannerContextService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacPlannerContextLookup lookup = await service.GetCurrentAsync(allyCode, cancellationToken);
            if (lookup.IsAvailable && lookup.Context is not null)
            {
                return Results.Ok(GacPlannerContextResponse.From(lookup.Context));
            }

            return ToUnavailableResult(lookup.Status, lookup.Message);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> AddOwnDefenseAsync(
        long allyCode,
        OwnDefenseMutationRequest request,
        IGacPlannerMutationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacPlannerLookup lookup = await service.AddOwnDefenseAsync(
                allyCode,
                request.Zone,
                request.TeamPresetId,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            return ToLookupResult(lookup);
        }
        catch (GacPlannerConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> RemoveOwnDefenseAsync(
        long allyCode,
        Guid assignmentId,
        DateTimeOffset? expectedUpdatedAtUtc,
        IGacPlannerMutationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacPlannerLookup lookup = await service.RemoveOwnDefenseAsync(
                allyCode,
                assignmentId,
                expectedUpdatedAtUtc,
                cancellationToken);
            return ToLookupResult(lookup);
        }
        catch (GacPlannerConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> AddVisibleDefenseAsync(
        long allyCode,
        VisibleDefenseMutationRequest request,
        IGacPlannerMutationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var defense = new SaveGacVisibleDefense(
                null,
                request.Zone,
                request.Label,
                request.LeaderDefinitionId,
                request.MemberDefinitionIds ?? [],
                request.IsFleet);
            GacPlannerLookup lookup = await service.AddVisibleDefenseAsync(
                allyCode,
                defense,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            return ToLookupResult(lookup);
        }
        catch (GacPlannerConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> RemoveVisibleDefenseAsync(
        long allyCode,
        Guid defenseId,
        DateTimeOffset? expectedUpdatedAtUtc,
        IGacPlannerMutationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacPlannerLookup lookup = await service.RemoveVisibleDefenseAsync(
                allyCode,
                defenseId,
                expectedUpdatedAtUtc,
                cancellationToken);
            return ToLookupResult(lookup);
        }
        catch (GacPlannerConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> AddAttackAsync(
        long allyCode,
        AddAttackMutationRequest request,
        IGacPlannerMutationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacPlannerLookup lookup = await service.AddAttackAsync(
                allyCode,
                request.DefenseId,
                request.TeamPresetId,
                request.Notes,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            return ToLookupResult(lookup);
        }
        catch (GacPlannerConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> UpdateAttackAsync(
        long allyCode,
        Guid attackId,
        UpdateAttackMutationRequest request,
        IGacPlannerMutationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacPlannerLookup lookup = await service.UpdateAttackAsync(
                allyCode,
                attackId,
                ParseAttackStatus(request.Status),
                request.Notes,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            return ToLookupResult(lookup);
        }
        catch (GacPlannerConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static IResult ToLookupResult(GacPlannerLookup lookup)
    {
        if (lookup.IsAvailable && lookup.State is not null)
        {
            return Results.Ok(GacPlannerEndpoints.GacPlannerResponse.From(lookup.State));
        }

        return ToUnavailableResult(lookup.Status, lookup.Message);
    }

    private static IResult ToUnavailableResult(CurrentGacOpponentStatus status, string? message)
    {
        var unavailable = new GacPlannerEndpoints.PlannerUnavailableResponse(status.ToString(), message);
        return status switch
        {
            CurrentGacOpponentStatus.NoActiveEvent or CurrentGacOpponentStatus.PlayerNotJoined =>
                Results.NotFound(unavailable),
            CurrentGacOpponentStatus.Pending => Results.Accepted(value: unavailable),
            CurrentGacOpponentStatus.OpponentUnavailable or CurrentGacOpponentStatus.FormatUnavailable =>
                Results.Conflict(unavailable),
            _ => Results.Problem(statusCode: StatusCodes.Status502BadGateway)
        };
    }

    private static GacAttackPlanStatus ParseAttackStatus(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Enum.TryParse(value.Trim(), ignoreCase: true, out GacAttackPlanStatus status) && Enum.IsDefined(status)
            ? status
            : throw new ArgumentException("Attack status must be Planned, Won, Failed or Cancelled.", nameof(value));
    }

    private static IResult Conflict(GacPlannerConcurrencyException exception) =>
        Results.Conflict(new GacPlannerEndpoints.PlannerUnavailableResponse("Conflict", exception.Message));

    private static IResult Validation(ArgumentException exception) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["planner"] = [exception.Message] });

    internal sealed record OwnDefenseMutationRequest(
        string Zone,
        Guid TeamPresetId,
        DateTimeOffset? ExpectedUpdatedAtUtc);

    internal sealed record VisibleDefenseMutationRequest(
        string Zone,
        string? Label,
        string LeaderDefinitionId,
        IReadOnlyCollection<string>? MemberDefinitionIds,
        bool IsFleet,
        DateTimeOffset? ExpectedUpdatedAtUtc);

    internal sealed record AddAttackMutationRequest(
        Guid DefenseId,
        Guid TeamPresetId,
        string? Notes,
        DateTimeOffset? ExpectedUpdatedAtUtc);

    internal sealed record UpdateAttackMutationRequest(
        string Status,
        string? Notes,
        DateTimeOffset? ExpectedUpdatedAtUtc);

    internal sealed record GacPlannerContextResponse(
        GacPlannerEndpoints.GacPlannerResponse Planner,
        RosterSnapshotResponse? PlayerRoster,
        RosterSnapshotResponse? OpponentRoster)
    {
        public static GacPlannerContextResponse From(GacPlannerContext context) => new(
            GacPlannerEndpoints.GacPlannerResponse.From(context.Planner),
            RosterSnapshotResponse.From(context.PlayerRoster),
            RosterSnapshotResponse.From(context.OpponentRoster));
    }

    internal sealed record RosterSnapshotResponse(
        long AllyCode,
        DateTimeOffset UpdatedAtUtc,
        string? PlayerName,
        long GalacticPower,
        int RosterCount,
        IReadOnlyCollection<PlayerEndpoints.EnrichedRosterUnitResponse> Items,
        IReadOnlyCollection<string> AvailableFactions)
    {
        public static RosterSnapshotResponse? From(PlayerRosterSnapshot? snapshot) => snapshot is null
            ? null
            : new RosterSnapshotResponse(
                snapshot.AllyCode,
                snapshot.UpdatedAtUtc,
                snapshot.PlayerName,
                snapshot.GalacticPower,
                snapshot.RosterCount,
                [.. snapshot.Units.Select(PlayerEndpoints.EnrichedRosterUnitResponse.From)],
                snapshot.AvailableFactions);
    }
}
