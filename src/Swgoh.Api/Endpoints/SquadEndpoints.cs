using Asp.Versioning;

using Swgoh.Application.Squads;
using Swgoh.Domain.Squads;

namespace Swgoh.Api.Endpoints;

internal static class SquadEndpoints
{
    public static IEndpointRouteBuilder MapSquadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("Squads");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/squads")
            .HasApiVersion(1.0)
            .WithTags("Squads");

        group.MapGet("", SearchAsync)
            .WithSummary("Search persisted squad definitions");
        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Get a squad definition");
        group.MapPost("", CreateAsync)
            .WithSummary("Create a squad definition");
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithSummary("Update a squad definition");
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Delete a squad definition");

        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        string? search,
        string? format,
        string? use,
        string? tag,
        int? limit,
        ISquadService service,
        CancellationToken cancellationToken)
    {
        try
        {
            SquadFormat? parsedFormat = string.IsNullOrWhiteSpace(format) ? null : ParseFormat(format);
            SquadUse? parsedUse = string.IsNullOrWhiteSpace(use) ? null : ParseUse(use);
            IReadOnlyCollection<SquadDetails> squads = await service.SearchAsync(
                new SquadSearchQuery(search, parsedFormat, parsedUse, tag, limit ?? 100),
                cancellationToken);
            return Results.Ok(squads.Select(SquadResponse.From));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ISquadService service,
        CancellationToken cancellationToken)
    {
        try
        {
            SquadDetails? squad = await service.GetAsync(id, cancellationToken);
            return squad is null ? Results.NotFound() : Results.Ok(SquadResponse.From(squad));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> CreateAsync(
        SaveSquadRequest request,
        ISquadService service,
        CancellationToken cancellationToken)
    {
        try
        {
            SquadDetails squad = await service.CreateAsync(ToInput(request), cancellationToken);
            return Results.Created($"/api/v1/squads/{squad.Id}", SquadResponse.From(squad));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        SaveSquadRequest request,
        ISquadService service,
        CancellationToken cancellationToken)
    {
        try
        {
            SquadDetails? squad = await service.UpdateAsync(id, ToInput(request), cancellationToken);
            return squad is null ? Results.NotFound() : Results.Ok(SquadResponse.From(squad));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISquadService service,
        CancellationToken cancellationToken)
    {
        try
        {
            bool deleted = await service.DeleteAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static SaveSquadDefinition ToInput(SaveSquadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SaveSquadDefinition(
            request.Name,
            ParseFormat(request.Format),
            ParseUse(request.Use),
            request.Tags ?? [],
            [.. (request.Variants ?? []).Select(variant => new SquadVariantInput(
                variant.Key,
                variant.Name,
                variant.LeaderDefinitionId,
                variant.MemberDefinitionIds ?? []))]);
    }

    private static SquadFormat ParseFormat(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant() switch
        {
            "3v3" or "threevsthree" or "3" => SquadFormat.ThreeVsThree,
            "5v5" or "fivevsfive" or "5" => SquadFormat.FiveVsFive,
            _ => throw new ArgumentException("Squad format must be 3v3 or 5v5.", nameof(value))
        };
    }

    private static SquadUse ParseUse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Enum.TryParse(value.Trim(), ignoreCase: true, out SquadUse use)
            ? use
            : throw new ArgumentException("Squad use must be Flexible, Offense or Defense.", nameof(value));
    }

    private static IResult Validation(ArgumentException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["squad"] = [exception.Message] });

    internal sealed record SaveSquadRequest(
        string Name,
        string Format,
        string Use,
        IReadOnlyCollection<string>? Tags,
        IReadOnlyCollection<SquadVariantRequest>? Variants);

    internal sealed record SquadVariantRequest(
        string Key,
        string Name,
        string LeaderDefinitionId,
        IReadOnlyCollection<string>? MemberDefinitionIds);

    internal sealed record SquadResponse(
        Guid Id,
        string Name,
        string Format,
        string Use,
        IReadOnlyCollection<string> Tags,
        IReadOnlyCollection<SquadVariantResponse> Variants,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc)
    {
        public static SquadResponse From(SquadDetails squad) => new(
            squad.Id,
            squad.Name,
            squad.Format == SquadFormat.ThreeVsThree ? "3v3" : "5v5",
            squad.Use.ToString(),
            squad.Tags,
            [.. squad.Variants.Select(SquadVariantResponse.From)],
            squad.CreatedAtUtc,
            squad.UpdatedAtUtc);
    }

    internal sealed record SquadVariantResponse(
        string Key,
        string Name,
        SquadUnitResponse Leader,
        IReadOnlyCollection<SquadUnitResponse> Members)
    {
        public static SquadVariantResponse From(SquadVariantDetails variant) => new(
            variant.Key,
            variant.Name,
            SquadUnitResponse.From(variant.Leader),
            [.. variant.Members.Select(SquadUnitResponse.From)]);
    }

    internal sealed record SquadUnitResponse(
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        IReadOnlyCollection<string> Factions)
    {
        public static SquadUnitResponse From(SquadUnitDetails unit) => new(
            unit.DefinitionId,
            unit.Name,
            unit.ThumbnailName,
            unit.Factions);
    }
}
