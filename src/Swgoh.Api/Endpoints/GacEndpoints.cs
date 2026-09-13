using Asp.Versioning;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Api.Endpoints;

internal static class GacEndpoints
{
    public static IEndpointRouteBuilder MapGacEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("GAC");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/gac")
            .HasApiVersion(1.0)
            .WithTags("GAC");

        group.MapGet("/defense-requirements", GetDefenseRequirements)
            .WithSummary("Get GAC defense requirements for a league and format");
        group.MapGet("/defense-requirements/transition", GetLeagueTransition)
            .WithSummary("Compare GAC defense requirements between two leagues");

        return endpoints;
    }

    private static IResult GetDefenseRequirements(
        string league,
        string format,
        IGacRulesService service)
    {
        try
        {
            GacDefenseRequirements requirements = service.GetDefenseRequirements(
                ParseLeague(league),
                ParseFormat(format));
            return Results.Ok(GacDefenseRequirementsResponse.From(requirements));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static IResult GetLeagueTransition(
        string fromLeague,
        string toLeague,
        string format,
        IGacRulesService service)
    {
        try
        {
            GacLeagueTransition transition = service.CompareLeagues(
                ParseLeague(fromLeague),
                ParseLeague(toLeague),
                ParseFormat(format));
            return Results.Ok(GacLeagueTransitionResponse.From(transition));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static GacLeague ParseLeague(string value)
    {
        if (!Enum.TryParse(value, ignoreCase: true, out GacLeague league) || !Enum.IsDefined(league))
        {
            throw new ArgumentException(
                "League must be Carbonite, Bronzium, Chromium, Aurodium or Kyber.",
                nameof(value));
        }

        return league;
    }

    private static GacFormat ParseFormat(string value) => value.Trim().ToLowerInvariant() switch
    {
        "3" or "3v3" or "threevsthree" => GacFormat.ThreeVsThree,
        "5" or "5v5" or "fivevsfive" => GacFormat.FiveVsFive,
        _ => throw new ArgumentException("Format must be 3v3 or 5v5.", nameof(value))
    };

    private static IResult Validation(ArgumentException exception) => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["gac"] = [exception.Message]
        });

    private sealed record GacDefenseRequirementsResponse(
        string League,
        string Format,
        int SquadDefenseCount,
        int FleetDefenseCount)
    {
        public static GacDefenseRequirementsResponse From(GacDefenseRequirements requirements) => new(
            requirements.League.ToString(),
            FormatName(requirements.Format),
            requirements.SquadDefenseCount,
            requirements.FleetDefenseCount);
    }

    private sealed record GacLeagueTransitionResponse(
        string FromLeague,
        string ToLeague,
        string Format,
        int FromSquadDefenseCount,
        int ToSquadDefenseCount,
        int FromFleetDefenseCount,
        int ToFleetDefenseCount,
        int SquadDefenseDelta,
        int FleetDefenseDelta,
        int AdditionalSquadDefenses,
        int AdditionalFleetDefenses,
        bool IsPromotion,
        bool IsDemotion)
    {
        public static GacLeagueTransitionResponse From(GacLeagueTransition transition) => new(
            transition.From.League.ToString(),
            transition.To.League.ToString(),
            FormatName(transition.To.Format),
            transition.From.SquadDefenseCount,
            transition.To.SquadDefenseCount,
            transition.From.FleetDefenseCount,
            transition.To.FleetDefenseCount,
            transition.SquadDefenseDelta,
            transition.FleetDefenseDelta,
            transition.AdditionalSquadDefenses,
            transition.AdditionalFleetDefenses,
            transition.IsPromotion,
            transition.IsDemotion);
    }

    private static string FormatName(GacFormat format) => format switch
    {
        GacFormat.ThreeVsThree => "3v3",
        GacFormat.FiveVsFive => "5v5",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.")
    };
}
