using Asp.Versioning;

using Swgoh.Application.Investments;

namespace Swgoh.Api.Endpoints;

internal static class InvestmentEndpoints
{
    public static IEndpointRouteBuilder MapInvestmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("Investments");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/investments/players/{allyCode:long}")
            .HasApiVersion(1.0)
            .WithTags("Investments");

        group.MapGet("/current", GetCurrentAsync)
            .WithSummary("Build a single investment priority list across GAC, RotE, Conquest, Era and Coliseum");
        group.MapGet("/inventory", GetInventoryAsync)
            .WithSummary("Get the persisted real-material inventory used by the investment optimizer");
        group.MapPut("/inventory", PutInventoryAsync)
            .WithSummary("Replace the persisted real-material inventory snapshot used by the investment optimizer");
        group.MapGet("/targets", GetTargetsAsync)
            .WithSummary("Get persistent player investment targets with live roster and inventory progress");
        group.MapGet("/targets/{definitionId}", GetTargetAsync)
            .WithSummary("Get one persistent investment target with live progress");
        group.MapPut("/targets/{definitionId}", PutTargetAsync)
            .WithSummary("Create or replace a persistent investment target for one unit");
        group.MapDelete("/targets/{definitionId}", DeleteTargetAsync)
            .WithSummary("Remove a persistent investment target");
        return endpoints;
    }

    private static async Task<IResult> GetCurrentAsync(
        long allyCode,
        IInvestmentOptimizerService service,
        CancellationToken cancellationToken)
    {
        try
        {
            InvestmentOptimizationResult? result = await service
                .GetCurrentAsync(allyCode, cancellationToken)
                .ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GetInventoryAsync(
        long allyCode,
        IPlayerInventoryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            PlayerInventorySnapshot? snapshot = await service
                .GetAsync(allyCode, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(ToInventoryResponse(snapshot));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> PutInventoryAsync(
        long allyCode,
        InventoryUpdateRequest request,
        IPlayerInventoryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            PlayerInventoryImport inventory = new(
                request.CapturedAtUtc,
                request.Source,
                [
                    .. request.Resources.Select(resource => new PlayerInventoryResource(
                        resource.Id,
                        resource.Name ?? resource.Id,
                        resource.Quantity))
                ]);
            PlayerInventorySnapshot saved = await service
                .ImportAsync(allyCode, inventory, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(ToInventoryResponse(saved));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GetTargetsAsync(
        long allyCode,
        IInvestmentTargetService service,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyCollection<InvestmentTargetProgress> targets = await service
                .GetAllAsync(allyCode, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(targets);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GetTargetAsync(
        long allyCode,
        string definitionId,
        IInvestmentTargetService service,
        CancellationToken cancellationToken)
    {
        try
        {
            InvestmentTargetProgress? target = await service
                .GetAsync(allyCode, definitionId, cancellationToken)
                .ConfigureAwait(false);
            return target is null ? Results.NotFound() : Results.Ok(target);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> PutTargetAsync(
        long allyCode,
        string definitionId,
        InvestmentTargetUpdateRequest request,
        IInvestmentTargetService service,
        CancellationToken cancellationToken)
    {
        try
        {
            InvestmentTargetProgress target = await service
                .SaveAsync(
                    allyCode,
                    definitionId,
                    new InvestmentTargetUpdate(request.TargetRelicTier, request.TargetStars),
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(target);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> DeleteTargetAsync(
        long allyCode,
        string definitionId,
        IInvestmentTargetService service,
        CancellationToken cancellationToken)
    {
        try
        {
            bool deleted = await service.DeleteAsync(allyCode, definitionId, cancellationToken).ConfigureAwait(false);
            return deleted ? Results.NoContent() : Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static InventoryResponse ToInventoryResponse(PlayerInventorySnapshot? snapshot)
    {
        IReadOnlyDictionary<string, long> quantities = snapshot?.Resources
            .ToDictionary(resource => resource.Id, resource => resource.Quantity, StringComparer.Ordinal)
            ?? new Dictionary<string, long>(StringComparer.Ordinal);

        return new InventoryResponse(
            snapshot is not null,
            snapshot?.CapturedAtUtc,
            snapshot?.Source,
            [
                .. PlayerInventoryCatalog.Resources
                    .OrderBy(resource => resource.SortOrder)
                    .Select(resource => new InventoryResourceResponse(
                        resource.Id,
                        resource.Name,
                        resource.Kind,
                        resource.SortOrder,
                        quantities.GetValueOrDefault(resource.Id)))
            ]);
    }

    private static IResult Validation(ArgumentException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "investment"] = [exception.Message]
        });

    private sealed record InventoryUpdateRequest(
        DateTimeOffset? CapturedAtUtc,
        string? Source,
        IReadOnlyCollection<InventoryResourceRequest> Resources);

    private sealed record InventoryResourceRequest(
        string Id,
        string? Name,
        long Quantity);

    private sealed record InvestmentTargetUpdateRequest(
        int? TargetRelicTier,
        int? TargetStars);

    private sealed record InventoryResponse(
        bool HasSnapshot,
        DateTimeOffset? CapturedAtUtc,
        string? Source,
        IReadOnlyCollection<InventoryResourceResponse> Resources);

    private sealed record InventoryResourceResponse(
        string Id,
        string Name,
        InventoryResourceKind Kind,
        int SortOrder,
        long Quantity);
}
