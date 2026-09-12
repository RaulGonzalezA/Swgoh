using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Api.Endpoints;

internal static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/players").WithTags("Players");
        group.MapGet("/{allyCode:long}", GetAsync);
        group.MapPut("/{allyCode:long}", PutAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(long allyCode, IPlayerProfileService service, CancellationToken cancellationToken)
    {
        PlayerProfile? player = await service.GetAsync(allyCode, cancellationToken);
        return player is null ? Results.NotFound() : Results.Ok(PlayerResponse.From(player));
    }

    private static async Task<IResult> PutAsync(long allyCode, SavePlayerRequest request, IPlayerProfileService service, CancellationToken cancellationToken)
    {
        try
        {
            PlayerProfile player = await service.SaveAsync(allyCode, request.Name, request.GalacticPower, cancellationToken);
            return Results.Ok(PlayerResponse.From(player));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["player"] = [exception.Message] });
        }
    }

    internal sealed record SavePlayerRequest(string Name, long GalacticPower);

    internal sealed record PlayerResponse(long AllyCode, string Name, long GalacticPower, DateTimeOffset UpdatedAtUtc)
    {
        public static PlayerResponse From(PlayerProfile player) => new(player.AllyCode, player.Name, player.GalacticPower, player.UpdatedAtUtc);
    }
}
