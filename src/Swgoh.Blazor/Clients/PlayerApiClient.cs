using System.Net;
using System.Text;

namespace Swgoh.Blazor.Clients;

public sealed class PlayerApiClient(HttpClient httpClient)
{
    private const int LegacyRosterGridPageSize = 24;
    private const int CompactRosterGridPageSize = 28;

    public async Task RefreshAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
            $"/api/v1/players/{allyCode}/refresh", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<PlayerViewModel?> GetAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync($"/api/v1/players/{allyCode}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlayerViewModel>(cancellationToken);
    }

    public async Task<PlayerRosterAnalysisViewModel?> GetAnalysisAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync($"/api/v1/players/{allyCode}/analysis", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlayerRosterAnalysisViewModel>(cancellationToken);
    }

    public async Task<RosterPageViewModel?> GetRosterAsync(
        long allyCode,
        int page = 1,
        int pageSize = 24,
        string? search = null,
        string type = "All",
        int? minRarity = null,
        int? minRelic = null,
        bool? hasZeta = null,
        bool? hasOmicron = null,
        string orderBy = "GalacticPower",
        string direction = "Descending",
        string? faction = null,
        CancellationToken cancellationToken = default)
    {
        int effectivePageSize = pageSize == LegacyRosterGridPageSize
            ? CompactRosterGridPageSize
            : pageSize;

        var query = new StringBuilder(
            $"?page={page}&pageSize={effectivePageSize}&type={Uri.EscapeDataString(type)}&orderBy={Uri.EscapeDataString(orderBy)}&direction={Uri.EscapeDataString(direction)}");

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Append("&search=").Append(Uri.EscapeDataString(search.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(faction))
        {
            query.Append("&faction=").Append(Uri.EscapeDataString(faction.Trim()));
        }

        AppendOptional(query, "minRarity", minRarity);
        AppendOptional(query, "minRelic", minRelic);
        AppendOptional(query, "hasZeta", hasZeta);
        AppendOptional(query, "hasOmicron", hasOmicron);

        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/players/{allyCode}/roster{query}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RosterPageViewModel>(cancellationToken);
    }

    public async Task<RosterUnitViewModel?> GetUnitAsync(
        long allyCode,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        string normalizedDefinitionId = definitionId.Trim();
        RosterPageViewModel? roster = await GetRosterAsync(
            allyCode,
            page: 1,
            pageSize: 100,
            search: normalizedDefinitionId,
            orderBy: "DefinitionId",
            direction: "Ascending",
            cancellationToken: cancellationToken);

        return roster?.Items.FirstOrDefault(unit =>
            string.Equals(unit.DefinitionId, normalizedDefinitionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(unit.Id, normalizedDefinitionId, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendOptional(StringBuilder query, string name, object? value)
    {
        if (value is null)
        {
            return;
        }

        string text = value is bool boolean
            ? boolean.ToString().ToLowerInvariant()
            : value.ToString() ?? string.Empty;
        query.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(text));
    }

    public sealed record PlayerViewModel(
        long AllyCode,
        string PlayerId,
        string Name,
        string? GuildName,
        int Level,
        long GalacticPower,
        DateTimeOffset UpdatedAtUtc,
        int RosterCount,
        int DatacronCount = 0);

    public sealed record PlayerRosterAnalysisViewModel(
        long AllyCode,
        long GalacticPower,
        long CharacterGalacticPower,
        long ShipGalacticPower,
        int CharacterCount,
        int ShipCount,
        int RelicCharacters,
        int Relic7Plus,
        int Relic8Plus,
        int Relic9Plus,
        int Relic10,
        int Zetas,
        int Omicrons,
        int FullyModdedCharacters,
        int UnmoddedCharacters,
        DateTimeOffset UpdatedAtUtc);

    public sealed record RosterPageViewModel(
        long AllyCode,
        DateTimeOffset UpdatedAtUtc,
        int Total,
        int Page,
        int PageSize,
        int TotalPages,
        IReadOnlyCollection<RosterUnitViewModel> Items,
        string? PlayerName = null,
        long GalacticPower = 0,
        int RosterCount = 0,
        IReadOnlyCollection<string>? AvailableFactions = null)
    {
        public IReadOnlyCollection<string> FactionOptions => AvailableFactions ?? [];
    }

    public sealed record RosterUnitViewModel(
        string Id,
        string DefinitionId,
        string Name,
        string? ThumbnailName,
        IReadOnlyCollection<string> Factions,
        int Level,
        int Rarity,
        int GearTier,
        int RelicTier,
        int EquippedModCount,
        long GalacticPower,
        bool IsShip,
        int ZetaCount,
        int OmicronCount,
        RosterUnitStatsViewModel? Stats = null,
        RosterModSummaryViewModel? Mods = null);

    public sealed record RosterUnitStatsViewModel(
        decimal? Health,
        decimal? Protection,
        decimal? Speed,
        decimal? PhysicalDamage,
        decimal? SpecialDamage,
        decimal? Armor,
        decimal? Resistance,
        decimal? Potency,
        decimal? Tenacity,
        decimal? CriticalDamage);

    public sealed record RosterModSummaryViewModel(
        int EquippedCount,
        int SixDotCount,
        int SpeedSetModCount,
        int SpeedPrimaryCount,
        decimal? SpeedBonus,
        bool IsComplete);
}
