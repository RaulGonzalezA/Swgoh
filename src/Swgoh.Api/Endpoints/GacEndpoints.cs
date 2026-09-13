using Asp.Versioning;

using Swgoh.Application.Gac;
using Swgoh.Application.Players;
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
        group.MapGet("/players/{allyCode:long}/current-opponent/scouting", GetCurrentOpponentScoutingAsync)
            .WithSummary("Detect the current GAC opponent and analyze only the active GAC format");
        group.MapPost("/opponents/{allyCode:long}/history", ImportHistoryAsync)
            .WithSummary("Import normalized historical GAC rounds for an opponent");
        group.MapGet("/opponents/{allyCode:long}/history", GetHistoryAsync)
            .WithSummary("Get persisted historical GAC rounds for an opponent");
        group.MapGet("/opponents/{allyCode:long}/scouting", GetScoutingAsync)
            .WithSummary("Analyze historical GAC behavior for an opponent");

        return endpoints;
    }

    private static IResult GetDefenseRequirements(string league, string format, IGacRulesService service)
    {
        try
        {
            GacDefenseRequirements requirements = service.GetDefenseRequirements(ParseLeague(league), ParseFormat(format));
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

    private static async Task<IResult> GetCurrentOpponentScoutingAsync(
        long allyCode,
        string? format,
        int? maxRounds,
        ICurrentGacScoutingService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacFormat? formatOverride = string.IsNullOrWhiteSpace(format) ? null : ParseFormat(format);
            CurrentGacScoutingResult result = await service.GetAsync(
                allyCode,
                formatOverride,
                maxRounds ?? 30,
                cancellationToken);

            if (result.Lookup.Status == CurrentGacOpponentStatus.Found && result.Lookup.Opponent is not null)
            {
                return Results.Ok(CurrentGacScoutingResponse.From(result));
            }

            CurrentGacLookupResponse unavailable = CurrentGacLookupResponse.From(result.Lookup);
            return result.Lookup.Status switch
            {
                CurrentGacOpponentStatus.NoActiveEvent or CurrentGacOpponentStatus.PlayerNotJoined =>
                    Results.NotFound(unavailable),
                CurrentGacOpponentStatus.OpponentUnavailable or CurrentGacOpponentStatus.FormatUnavailable =>
                    Results.Conflict(unavailable),
                _ => Results.Problem(statusCode: StatusCodes.Status502BadGateway)
            };
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> ImportHistoryAsync(
        long allyCode,
        ImportHistoryRequest request,
        IGacHistoryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            GacHistoryRoundInput[] rounds =
            [
                .. (request.Rounds ?? []).Select(ToInput)
            ];
            GacHistoryImportResult result = await service.ImportAsync(allyCode, rounds, cancellationToken);
            return Results.Ok(result);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GetHistoryAsync(
        long allyCode,
        string? format,
        int? maxRounds,
        IGacHistoryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacFormat? parsedFormat = string.IsNullOrWhiteSpace(format) ? null : ParseFormat(format);
            IReadOnlyCollection<GacHistoricalRound> history = await service.GetAsync(
                allyCode,
                new GacHistoryQuery(parsedFormat, maxRounds ?? 30),
                cancellationToken);
            return Results.Ok(history.Select(GacHistoricalRoundResponse.From));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static async Task<IResult> GetScoutingAsync(
        long allyCode,
        string format,
        string? targetLeague,
        int? maxRounds,
        IOpponentScoutingService service,
        CancellationToken cancellationToken)
    {
        try
        {
            GacLeague? league = string.IsNullOrWhiteSpace(targetLeague) ? null : ParseLeague(targetLeague);
            OpponentScoutingReport? report = await service.GetAsync(
                allyCode,
                ParseFormat(format),
                league,
                maxRounds ?? 30,
                cancellationToken);
            return report is null
                ? Results.NotFound()
                : Results.Ok(OpponentScoutingResponse.From(report));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception);
        }
    }

    private static GacHistoryRoundInput ToInput(ImportRoundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new GacHistoryRoundInput(
            request.Season,
            request.EventNumber,
            request.RoundNumber,
            ParseFormat(request.Format),
            ParseLeague(request.League),
            request.StartedAtUtc,
            request.FullClear,
            string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source,
            [.. (request.Defenses ?? []).Select(defense => new GacHistoryDefenseInput(
                defense.Zone,
                ToInput(defense.Squad),
                defense.Holds,
                defense.Defeated))],
            [.. (request.OffenseBattles ?? []).Select(battle => new GacHistoryOffenseBattleInput(
                battle.Zone,
                ToInput(battle.Defender),
                ToInput(battle.Attacker),
                battle.Won,
                battle.Banners,
                battle.Attempt,
                battle.AttackedAtUtc))]);
    }

    private static GacHistorySquadInput ToInput(ImportSquadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new GacHistorySquadInput(
            request.LeaderDefinitionId,
            request.MemberDefinitionIds ?? [],
            request.IsFleet);
    }

    private static GacLeague ParseLeague(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out GacLeague league) || !Enum.IsDefined(league))
        {
            throw new ArgumentException(
                "League must be Carbonite, Bronzium, Chromium, Aurodium or Kyber.",
                nameof(value));
        }

        return league;
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

    private static IResult Validation(ArgumentException exception) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["gac"] = [exception.Message] });

    internal sealed record ImportHistoryRequest(IReadOnlyCollection<ImportRoundRequest>? Rounds);

    internal sealed record ImportRoundRequest(
        int Season,
        int EventNumber,
        int RoundNumber,
        string Format,
        string League,
        DateTimeOffset StartedAtUtc,
        bool? FullClear,
        string? Source,
        IReadOnlyCollection<ImportDefenseRequest>? Defenses,
        IReadOnlyCollection<ImportOffenseBattleRequest>? OffenseBattles);

    internal sealed record ImportDefenseRequest(
        string Zone,
        ImportSquadRequest Squad,
        int Holds,
        bool Defeated);

    internal sealed record ImportOffenseBattleRequest(
        string Zone,
        ImportSquadRequest Defender,
        ImportSquadRequest Attacker,
        bool Won,
        int Banners,
        int Attempt,
        DateTimeOffset? AttackedAtUtc);

    internal sealed record ImportSquadRequest(
        string LeaderDefinitionId,
        IReadOnlyCollection<string>? MemberDefinitionIds,
        bool IsFleet);

    internal sealed record GacHistoricalRoundResponse(
        string Id,
        long AllyCode,
        int Season,
        int EventNumber,
        int RoundNumber,
        string Format,
        string League,
        DateTimeOffset StartedAtUtc,
        bool? FullClear,
        string Source,
        IReadOnlyCollection<GacDefensePlacement> Defenses,
        IReadOnlyCollection<GacOffenseBattle> OffenseBattles)
    {
        public static GacHistoricalRoundResponse From(GacHistoricalRound round) => new(
            round.Id,
            round.AllyCode,
            round.Season,
            round.EventNumber,
            round.RoundNumber,
            FormatName(round.Format),
            round.League.ToString(),
            round.StartedAtUtc,
            round.FullClear,
            round.Source,
            round.Defenses,
            round.OffenseBattles);
    }

    internal sealed record CurrentGacScoutingResponse(
        CurrentGacOpponentResponse Opponent,
        OpponentScoutingResponse? Scouting,
        CurrentOpponentRosterScoutingResponse? RosterScouting)
    {
        public static CurrentGacScoutingResponse From(CurrentGacScoutingResult result)
        {
            CurrentGacOpponent opponent = result.Lookup.Opponent
                ?? throw new InvalidOperationException("A found lookup must contain an opponent.");
            return new CurrentGacScoutingResponse(
                CurrentGacOpponentResponse.From(opponent),
                result.Scouting is null ? null : OpponentScoutingResponse.From(result.Scouting),
                result.RosterScouting is null ? null : CurrentOpponentRosterScoutingResponse.From(result.RosterScouting));
        }
    }

    internal sealed record CurrentOpponentRosterScoutingResponse(
        PlayerRosterAnalysis Analysis,
        IReadOnlyCollection<PlayerRosterUnit> GalacticLegends,
        IReadOnlyCollection<PlayerRosterUnit> TopCharacters,
        IReadOnlyCollection<PlayerRosterUnit> TopShips,
        IReadOnlyCollection<PlayerRosterUnit> OmicronCharacters)
    {
        public static CurrentOpponentRosterScoutingResponse From(CurrentOpponentRosterScouting scouting) => new(
            scouting.Analysis,
            scouting.GalacticLegends,
            scouting.TopCharacters,
            scouting.TopShips,
            scouting.OmicronCharacters);
    }

    internal sealed record CurrentGacOpponentResponse(
        long PlayerAllyCode,
        long OpponentAllyCode,
        string OpponentName,
        string? OpponentPlayerId,
        string League,
        string Format,
        string EventId,
        string EventInstanceId,
        string BracketId,
        int? RoundNumber,
        string FormatSource,
        string OpponentResolutionMethod)
    {
        public static CurrentGacOpponentResponse From(CurrentGacOpponent opponent) => new(
            opponent.PlayerAllyCode,
            opponent.OpponentAllyCode,
            opponent.OpponentName,
            opponent.OpponentPlayerId,
            opponent.League.ToString(),
            FormatName(opponent.Format),
            opponent.EventId,
            opponent.EventInstanceId,
            opponent.BracketId,
            opponent.RoundNumber,
            opponent.FormatSource,
            opponent.OpponentResolutionMethod);
    }

    internal sealed record CurrentGacLookupResponse(string Status, string? Message)
    {
        public static CurrentGacLookupResponse From(CurrentGacOpponentLookup lookup) =>
            new(lookup.Status.ToString(), lookup.Message);
    }

    internal sealed record OpponentScoutingResponse(
        long AllyCode,
        string Format,
        int RoundsAnalyzed,
        int SeasonsAnalyzed,
        DateTimeOffset? EarliestRoundUtc,
        DateTimeOffset? LatestRoundUtc,
        string? LatestObservedLeague,
        string TargetLeague,
        int RequiredSquadDefenses,
        int RequiredFleetDefenses,
        int AdditionalUnobservedSquadSlots,
        int AdditionalUnobservedFleetSlots,
        decimal? FullClearRate,
        decimal? AverageFirstAttackDelayMinutes,
        IReadOnlyCollection<GacDefensePatternDetails> DefensePatterns,
        IReadOnlyCollection<GacCounterPatternDetails> CounterPatterns,
        IReadOnlyCollection<GacPredictedDefenseDetails> PredictedSquadDefenses,
        IReadOnlyCollection<GacPredictedDefenseDetails> PredictedFleetDefenses)
    {
        public static OpponentScoutingResponse From(OpponentScoutingReport report) => new(
            report.AllyCode,
            FormatName(report.Format),
            report.RoundsAnalyzed,
            report.SeasonsAnalyzed,
            report.EarliestRoundUtc,
            report.LatestRoundUtc,
            report.LatestObservedLeague?.ToString(),
            report.TargetLeague.ToString(),
            report.RequiredSquadDefenses,
            report.RequiredFleetDefenses,
            report.AdditionalUnobservedSquadSlots,
            report.AdditionalUnobservedFleetSlots,
            report.FullClearRate,
            report.AverageFirstAttackDelayMinutes,
            report.DefensePatterns,
            report.CounterPatterns,
            report.PredictedSquadDefenses,
            report.PredictedFleetDefenses);
    }

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
