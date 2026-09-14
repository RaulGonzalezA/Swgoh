using Asp.Versioning;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Api.Endpoints;

internal static class GacPlannerEndpoints
{
    public static IEndpointRouteBuilder MapGacPlannerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("GAC Planner");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/gac/players/{allyCode:long}/planner")
            .HasApiVersion(1.0)
            .WithTags("GAC Planner");

        group.MapGet("/current", GetCurrentAsync)
            .WithSummary("Get the persisted attack plan for the current GAC round");
        group.MapPut("/current", SaveCurrentAsync)
            .WithSummary("Save visible enemy defenses, own defenses and attack assignments for the current GAC round");
        group.MapPost("/presets", CreatePresetAsync)
            .WithSummary("Create a reusable personal GAC team preset");
        group.MapPut("/presets/{id:guid}", UpdatePresetAsync)
            .WithSummary("Update a reusable personal GAC team preset");
        group.MapDelete("/presets/{id:guid}", DeletePresetAsync)
            .WithSummary("Delete a reusable personal GAC team preset");

        return endpoints;
    }

    private static async Task<IResult> GetCurrentAsync(
        long allyCode,
        IGacPlannerService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacPlannerLookup lookup = await service.GetCurrentAsync(allyCode, cancellationToken);
            return ToLookupResult(lookup);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> SaveCurrentAsync(
        long allyCode,
        SavePlanRequest request,
        IGacPlannerService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            SaveCurrentGacRoundPlan input = new(
                [.. (request.OwnDefenses ?? []).Select(item => new SaveGacOwnDefenseAssignment(
                    item.Id,
                    item.Zone,
                    item.TeamPresetId))],
                [.. (request.VisibleDefenses ?? []).Select(item => new SaveGacVisibleDefense(
                    item.Id,
                    item.Zone,
                    item.Label,
                    item.LeaderDefinitionId,
                    item.MemberDefinitionIds ?? [],
                    item.IsFleet))],
                [.. (request.Attacks ?? []).Select(item => new SaveGacAttackAssignment(
                    item.Id,
                    item.DefenseId,
                    item.TeamPresetId,
                    item.Attempt,
                    ParseAttackStatus(item.Status),
                    item.Notes))]);
            GacPlannerLookup lookup = await service.SaveCurrentAsync(allyCode, input, cancellationToken);
            return ToLookupResult(lookup);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> CreatePresetAsync(
        long allyCode,
        SavePresetRequest request,
        IGacPlannerService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacTeamPresetDetails preset = await service.CreatePresetAsync(
                allyCode,
                ToInput(request),
                cancellationToken);
            return Results.Created(
                $"/api/v1/gac/players/{allyCode}/planner/presets/{preset.Id}",
                TeamPresetResponse.From(preset));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> UpdatePresetAsync(
        long allyCode,
        Guid id,
        SavePresetRequest request,
        IGacPlannerService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacTeamPresetDetails? preset = await service.UpdatePresetAsync(
                allyCode,
                id,
                ToInput(request),
                cancellationToken);
            return preset is null ? Results.NotFound() : Results.Ok(TeamPresetResponse.From(preset));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> DeletePresetAsync(
        long allyCode,
        Guid id,
        IGacPlannerService service,
        CancellationToken cancellationToken)
    {
        try
        {
            bool deleted = await service.DeletePresetAsync(allyCode, id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static SaveGacTeamPreset ToInput(SavePresetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SaveGacTeamPreset(
            request.Name,
            ParseFormat(request.Format),
            ParseTeamUse(request.Use),
            request.LeaderDefinitionId,
            request.MemberDefinitionIds ?? [],
            request.IsFleet);
    }

    private static IResult ToLookupResult(GacPlannerLookup lookup)
    {
        if (lookup.IsAvailable && lookup.State is not null)
        {
            return Results.Ok(GacPlannerResponse.From(lookup.State));
        }

        var unavailable = new PlannerUnavailableResponse(lookup.Status.ToString(), lookup.Message);
        return lookup.Status switch
        {
            CurrentGacOpponentStatus.NoActiveEvent or CurrentGacOpponentStatus.PlayerNotJoined =>
                Results.NotFound(unavailable),
            CurrentGacOpponentStatus.Pending => Results.Accepted(value: unavailable),
            CurrentGacOpponentStatus.OpponentUnavailable or CurrentGacOpponentStatus.FormatUnavailable =>
                Results.Conflict(unavailable),
            _ => Results.Problem(statusCode: StatusCodes.Status502BadGateway)
        };
    }

    private static GacFormat ParseFormat(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant() switch
        {
            "3" or "3v3" or "threevsthree" => GacFormat.ThreeVsThree,
            "5" or "5v5" or "fivevsfive" => GacFormat.FiveVsFive,
            _ => throw new ArgumentException("Format must be 3v3 or 5v5.", nameof(value))
        };
    }

    private static GacPlannerTeamUse ParseTeamUse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Enum.TryParse(value.Trim(), ignoreCase: true, out GacPlannerTeamUse use) && Enum.IsDefined(use)
            ? use
            : throw new ArgumentException("Team use must be Flexible, Offense or Defense.", nameof(value));
    }

    private static GacAttackPlanStatus ParseAttackStatus(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Enum.TryParse(value.Trim(), ignoreCase: true, out GacAttackPlanStatus status) && Enum.IsDefined(status)
            ? status
            : throw new ArgumentException("Attack status must be Planned, Won, Failed or Cancelled.", nameof(value));
    }

    private static string FormatName(GacFormat format) => format switch
    {
        GacFormat.ThreeVsThree => "3v3",
        GacFormat.FiveVsFive => "5v5",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.")
    };

    private static IResult Validation(ArgumentException exception) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["planner"] = [exception.Message] });

    internal sealed record SavePresetRequest(
        string Name,
        string Format,
        string Use,
        string LeaderDefinitionId,
        IReadOnlyCollection<string>? MemberDefinitionIds,
        bool IsFleet);

    internal sealed record SavePlanRequest(
        IReadOnlyCollection<OwnDefenseRequest>? OwnDefenses,
        IReadOnlyCollection<VisibleDefenseRequest>? VisibleDefenses,
        IReadOnlyCollection<AttackRequest>? Attacks);

    internal sealed record OwnDefenseRequest(Guid? Id, string Zone, Guid TeamPresetId);

    internal sealed record VisibleDefenseRequest(
        Guid? Id,
        string Zone,
        string? Label,
        string LeaderDefinitionId,
        IReadOnlyCollection<string>? MemberDefinitionIds,
        bool IsFleet);

    internal sealed record AttackRequest(
        Guid? Id,
        Guid DefenseId,
        Guid TeamPresetId,
        int Attempt,
        string Status,
        string? Notes);

    internal sealed record PlannerUnavailableResponse(string Status, string? Message);

    internal sealed record GacPlannerResponse(
        PlannerOpponentResponse Opponent,
        IReadOnlyCollection<TeamPresetResponse> Presets,
        RoundPlanResponse Plan)
    {
        public static GacPlannerResponse From(GacPlannerState state) => new(
            PlannerOpponentResponse.From(state.Opponent),
            [.. state.Presets.Select(TeamPresetResponse.From)],
            RoundPlanResponse.From(state.Plan));
    }

    internal sealed record PlannerOpponentResponse(
        long OpponentAllyCode,
        string OpponentName,
        string League,
        string Format,
        int? RoundNumber)
    {
        public static PlannerOpponentResponse From(CurrentGacOpponent opponent) => new(
            opponent.OpponentAllyCode,
            opponent.OpponentName,
            opponent.League.ToString(),
            FormatName(opponent.Format),
            opponent.RoundNumber);
    }

    internal sealed record RoundPlanResponse(
        string Id,
        long PlayerAllyCode,
        long OpponentAllyCode,
        string OpponentName,
        string EventId,
        string EventInstanceId,
        int RoundNumber,
        string Format,
        string League,
        IReadOnlyCollection<OwnDefenseResponse> OwnDefenses,
        IReadOnlyCollection<VisibleDefenseResponse> VisibleDefenses,
        IReadOnlyCollection<AttackResponse> Attacks,
        IReadOnlyCollection<ConflictResponse> Conflicts,
        IReadOnlyCollection<CounterHintResponse> CounterHints,
        DateTimeOffset UpdatedAtUtc)
    {
        public static RoundPlanResponse From(GacRoundPlanDetails plan) => new(
            plan.Id,
            plan.PlayerAllyCode,
            plan.OpponentAllyCode,
            plan.OpponentName,
            plan.EventId,
            plan.EventInstanceId,
            plan.RoundNumber,
            FormatName(plan.Format),
            plan.League.ToString(),
            [.. plan.OwnDefenses.Select(OwnDefenseResponse.From)],
            [.. plan.VisibleDefenses.Select(VisibleDefenseResponse.From)],
            [.. plan.Attacks.Select(AttackResponse.From)],
            [.. plan.Conflicts.Select(ConflictResponse.From)],
            [.. plan.CounterHints.Select(CounterHintResponse.From)],
            plan.UpdatedAtUtc);
    }

    internal sealed record OwnDefenseResponse(Guid Id, string Zone, TeamPresetResponse Team)
    {
        public static OwnDefenseResponse From(GacOwnDefenseAssignmentDetails assignment) =>
            new(assignment.Id, assignment.Zone, TeamPresetResponse.From(assignment.Team));
    }

    internal sealed record VisibleDefenseResponse(
        Guid Id,
        string Zone,
        string? Label,
        PlannerSquadResponse Squad,
        bool Defeated)
    {
        public static VisibleDefenseResponse From(GacVisibleDefenseDetails defense) => new(
            defense.Id,
            defense.Zone,
            defense.Label,
            PlannerSquadResponse.From(defense.Squad),
            defense.Defeated);
    }

    internal sealed record AttackResponse(
        Guid Id,
        Guid DefenseId,
        TeamPresetResponse Team,
        int Attempt,
        string Status,
        string? Notes,
        int? Banners)
    {
        public static AttackResponse From(GacAttackAssignmentDetails attack) => new(
            attack.Id,
            attack.DefenseId,
            TeamPresetResponse.From(attack.Team),
            attack.Attempt,
            attack.Status.ToString(),
            attack.Notes,
            attack.Banners);
    }

    internal sealed record ConflictResponse(
        string Code,
        string Severity,
        string Message,
        IReadOnlyCollection<Guid> RelatedAssignmentIds,
        IReadOnlyCollection<string> UnitDefinitionIds)
    {
        public static ConflictResponse From(GacPlannerConflict conflict) => new(
            conflict.Code,
            conflict.Severity,
            conflict.Message,
            conflict.RelatedAssignmentIds,
            conflict.UnitDefinitionIds);
    }

    internal sealed record CounterHintResponse(
        Guid DefenseId,
        string ThreatName,
        string Confidence,
        string Source,
        string Rationale,
        bool RequiresDatacronVerification,
        Guid? MatchingTeamPresetId,
        IReadOnlyCollection<PlannerUnitResponse> RecommendedTeam,
        int? Uses,
        decimal? WinRate,
        decimal? OneShotRate,
        decimal? AverageBanners,
        int? PlayersObserved)
    {
        public static CounterHintResponse From(GacPlannerCounterHint hint) => new(
            hint.DefenseId,
            hint.ThreatName,
            hint.Confidence,
            hint.Source,
            hint.Rationale,
            hint.RequiresDatacronVerification,
            hint.MatchingTeamPresetId,
            [.. hint.RecommendedTeam.Select(PlannerUnitResponse.From)],
            hint.Uses,
            hint.WinRate,
            hint.OneShotRate,
            hint.AverageBanners,
            hint.PlayersObserved);
    }

    internal sealed record TeamPresetResponse(
        Guid Id,
        long AllyCode,
        string Name,
        string Format,
        string Use,
        PlannerSquadResponse Squad,
        DateTimeOffset UpdatedAtUtc)
    {
        public static TeamPresetResponse From(GacTeamPresetDetails preset) => new(
            preset.Id,
            preset.AllyCode,
            preset.Name,
            FormatName(preset.Format),
            preset.Use.ToString(),
            PlannerSquadResponse.From(preset.Squad),
            preset.UpdatedAtUtc);
    }

    internal sealed record PlannerSquadResponse(
        PlannerUnitResponse Leader,
        IReadOnlyCollection<PlannerUnitResponse> Members,
        bool IsFleet)
    {
        public static PlannerSquadResponse From(GacPlannerSquadDetails squad) => new(
            PlannerUnitResponse.From(squad.Leader),
            [.. squad.Members.Select(PlannerUnitResponse.From)],
            squad.IsFleet);
    }

    internal sealed record PlannerUnitResponse(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        bool IsShip,
        long? GalacticPower,
        int? RelicTier,
        int? ZetaCount,
        int? OmicronCount)
    {
        public static PlannerUnitResponse From(GacPlannerUnitDetails unit) => new(
            unit.DefinitionId,
            unit.Name,
            unit.ThumbnailName,
            unit.IsShip,
            unit.GalacticPower,
            unit.RelicTier,
            unit.ZetaCount,
            unit.OmicronCount);
    }
}
