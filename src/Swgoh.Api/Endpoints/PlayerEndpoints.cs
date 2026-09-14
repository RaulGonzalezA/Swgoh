using Asp.Versioning;

using Microsoft.AspNetCore.RateLimiting;

using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Api.Endpoints;

internal static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versionedApi = endpoints.NewVersionedApi("Players");
        RouteGroupBuilder group = versionedApi
            .MapGroup("/api/v{version:apiVersion}/players")
            .HasApiVersion(1.0)
            .WithTags("Players");

        group.MapGet("/{allyCode:long}", GetAsync)
            .WithSummary("Get the persisted player profile");
        group.MapGet("/{allyCode:long}/roster", GetRosterAsync)
            .WithSummary("Get an enriched, filtered, sorted and paged player roster");
        group.MapGet("/{allyCode:long}/analysis", GetAnalysisAsync)
            .WithSummary("Get roster analysis metrics");
        group.MapGet("/{allyCode:long}/history", GetHistoryAsync)
            .WithSummary("Get recent player snapshots");
        group.MapGet("/{allyCode:long}/gl-progress", GetGalacticLegendProgressAsync)
            .WithSummary("Get Galactic Legend requirement progress");
        group.MapPost("/{allyCode:long}/refresh", RefreshAsync)
            .WithSummary("Refresh a player from live SWGOH data")
            .RequireRateLimiting("player-refresh")
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPut("/{allyCode:long}", PutAsync)
            .WithSummary("Create or update a player manually");

        return endpoints;
    }

    private static async Task<IResult> GetAsync(long allyCode, IPlayerProfileService service, CancellationToken cancellationToken)
    {
        PlayerProfile? player = await service.GetAsync(allyCode, cancellationToken);
        return player is null ? Results.NotFound() : Results.Ok(PlayerResponse.From(player));
    }

    private static async Task<IResult> GetRosterAsync(
        long allyCode,
        int? page,
        int? pageSize,
        string? search,
        PlayerRosterUnitType? type,
        int? minRarity,
        int? minRelic,
        bool? hasZeta,
        bool? hasOmicron,
        string? faction,
        PlayerRosterSortField? orderBy,
        PlayerRosterSortDirection? direction,
        IPlayerRosterService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new PlayerRosterQuery(
                Page: page ?? 1,
                PageSize: pageSize ?? 50,
                Search: search,
                Type: type ?? PlayerRosterUnitType.All,
                MinRarity: minRarity,
                MinRelic: minRelic,
                HasZeta: hasZeta,
                HasOmicron: hasOmicron,
                OrderBy: orderBy ?? PlayerRosterSortField.GalacticPower,
                Direction: direction ?? PlayerRosterSortDirection.Descending,
                Faction: faction);

            PlayerRosterPage? roster = await service.GetAsync(allyCode, query, cancellationToken);
            return roster is null ? Results.NotFound() : Results.Ok(RosterPageResponse.From(roster));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["roster"] = [exception.Message] });
        }
    }

    private static async Task<IResult> GetAnalysisAsync(
        long allyCode,
        IPlayerAnalysisService service,
        CancellationToken cancellationToken)
    {
        PlayerRosterAnalysis? analysis = await service.GetAsync(allyCode, cancellationToken);
        return analysis is null ? Results.NotFound() : Results.Ok(analysis);
    }

    private static async Task<IResult> GetHistoryAsync(
        long allyCode,
        int? limit,
        IPlayerHistoryService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<PlayerSnapshot> snapshots = await service.GetRecentAsync(allyCode, limit ?? 30, cancellationToken);
        return Results.Ok(snapshots);
    }

    private static async Task<IResult> GetGalacticLegendProgressAsync(
        long allyCode,
        IGalacticLegendProgressService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<GalacticLegendProgress>? progress = await service.GetAsync(allyCode, cancellationToken);
        return progress is null ? Results.NotFound() : Results.Ok(progress);
    }

    private static async Task<IResult> RefreshAsync(
        long allyCode,
        IPlayerProfileService service,
        CancellationToken cancellationToken)
    {
        try
        {
            PlayerProfile player = await service.RefreshFromGameAsync(allyCode, cancellationToken);
            return Results.Ok(PlayerResponse.From(player));
        }
        catch (HttpRequestException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["player"] = [exception.Message] });
        }
    }

    private static async Task<IResult> PutAsync(
        long allyCode,
        SavePlayerRequest request,
        IPlayerProfileService service,
        CancellationToken cancellationToken)
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

    internal sealed record PlayerResponse(
        long AllyCode,
        string PlayerId,
        string Name,
        string? GuildId,
        string? GuildName,
        int Level,
        long GalacticPower,
        DateTimeOffset UpdatedAtUtc,
        int RosterCount,
        IReadOnlyCollection<RosterUnitResponse> Roster,
        IReadOnlyCollection<PlayerDatacronResponse> Datacrons)
    {
        public static PlayerResponse From(PlayerProfile player) => new(
            player.AllyCode,
            player.PlayerId,
            player.Name,
            player.GuildId,
            player.GuildName,
            player.Level,
            player.GalacticPower,
            player.UpdatedAtUtc,
            player.Roster.Count,
            [.. player.Roster.Select(RosterUnitResponse.From)],
            [.. player.Datacrons.Select(PlayerDatacronResponse.From)]);
    }

    internal sealed record RosterPageResponse(
        long AllyCode,
        DateTimeOffset UpdatedAtUtc,
        int Total,
        int Page,
        int PageSize,
        int TotalPages,
        IReadOnlyCollection<EnrichedRosterUnitResponse> Items,
        string? PlayerName,
        long GalacticPower,
        int RosterCount,
        IReadOnlyCollection<string> AvailableFactions)
    {
        public static RosterPageResponse From(PlayerRosterPage roster) => new(
            roster.AllyCode,
            roster.UpdatedAtUtc,
            roster.Total,
            roster.Page,
            roster.PageSize,
            roster.TotalPages,
            [.. roster.Items.Select(EnrichedRosterUnitResponse.From)],
            roster.PlayerName,
            roster.GalacticPower,
            roster.RosterCount,
            roster.FactionOptions);
    }

    internal sealed record EnrichedRosterUnitResponse(
        string Id,
        string DefinitionId,
        string Name,
        string? NameKey,
        string? ThumbnailName,
        IReadOnlyCollection<string> Factions,
        IReadOnlyCollection<string> Tags,
        int Level,
        int Rarity,
        int GearTier,
        int RelicTier,
        int EquippedModCount,
        long GalacticPower,
        bool IsShip,
        int ZetaCount,
        int OmicronCount,
        RosterUnitStatsResponse? Stats,
        RosterModSummaryResponse? Mods)
    {
        public static EnrichedRosterUnitResponse From(PlayerRosterUnit unit) => new(
            unit.Id,
            unit.DefinitionId,
            unit.Name,
            unit.NameKey,
            unit.ThumbnailName,
            unit.Factions,
            unit.Tags,
            unit.Level,
            unit.Rarity,
            unit.GearTier,
            unit.RelicTier,
            unit.EquippedModCount,
            unit.GalacticPower,
            unit.IsShip,
            unit.ZetaCount,
            unit.OmicronCount,
            RosterUnitStatsResponse.From(unit.Stats),
            RosterModSummaryResponse.From(unit.Mods));
    }

    internal sealed record RosterUnitResponse(
        string Id,
        string DefinitionId,
        int Level,
        int Rarity,
        int GearTier,
        int RelicTier,
        int EquippedModCount,
        long GalacticPower,
        bool IsShip,
        int ZetaCount,
        int OmicronCount,
        RosterUnitStatsResponse? Stats,
        RosterModSummaryResponse? Mods)
    {
        public static RosterUnitResponse From(RosterUnit unit) => new(
            unit.Id,
            unit.DefinitionId,
            unit.Level,
            unit.Rarity,
            unit.GearTier,
            unit.RelicTier,
            unit.EquippedModCount,
            unit.GalacticPower,
            unit.IsShip,
            unit.ZetaCount,
            unit.OmicronCount,
            RosterUnitStatsResponse.From(unit.Stats),
            RosterModSummaryResponse.From(unit.Mods));
    }

    internal sealed record RosterUnitStatsResponse(
        decimal? Health,
        decimal? Protection,
        decimal? Speed,
        decimal? PhysicalDamage,
        decimal? SpecialDamage,
        decimal? Armor,
        decimal? Resistance,
        decimal? Potency,
        decimal? Tenacity,
        decimal? CriticalDamage)
    {
        public static RosterUnitStatsResponse? From(RosterUnitStats? stats) => stats is null
            ? null
            : new RosterUnitStatsResponse(
                stats.Health,
                stats.Protection,
                stats.Speed,
                stats.PhysicalDamage,
                stats.SpecialDamage,
                stats.Armor,
                stats.Resistance,
                stats.Potency,
                stats.Tenacity,
                stats.CriticalDamage);
    }

    internal sealed record RosterModSummaryResponse(
        int EquippedCount,
        int SixDotCount,
        int SpeedSetModCount,
        int SpeedPrimaryCount,
        decimal? SpeedBonus,
        bool IsComplete)
    {
        public static RosterModSummaryResponse? From(RosterModSummary? mods) => mods is null
            ? null
            : new RosterModSummaryResponse(
                mods.EquippedCount,
                mods.SixDotCount,
                mods.SpeedSetModCount,
                mods.SpeedPrimaryCount,
                mods.SpeedBonus,
                mods.IsComplete);
    }

    internal sealed record PlayerDatacronResponse(
        string Id,
        string SetId,
        string TemplateId,
        int Tier,
        bool Locked,
        int HighestRequiredRelicTier,
        bool HasAbilityAffix,
        IReadOnlyCollection<PlayerDatacronAffixResponse> Affixes)
    {
        public static PlayerDatacronResponse From(PlayerDatacron datacron) => new(
            datacron.Id,
            datacron.SetId,
            datacron.TemplateId,
            datacron.Tier,
            datacron.Locked,
            datacron.HighestRequiredRelicTier,
            datacron.HasAbilityAffix,
            [.. datacron.Affixes.Select(PlayerDatacronAffixResponse.From)]);
    }

    internal sealed record PlayerDatacronAffixResponse(
        string? AbilityId,
        int? StatType,
        long? StatValue,
        int? RequiredRelicTier,
        IReadOnlyCollection<string> Tags)
    {
        public static PlayerDatacronAffixResponse From(PlayerDatacronAffix affix) => new(
            affix.AbilityId,
            affix.StatType,
            affix.StatValue,
            affix.RequiredRelicTier,
            affix.Tags);
    }
}
