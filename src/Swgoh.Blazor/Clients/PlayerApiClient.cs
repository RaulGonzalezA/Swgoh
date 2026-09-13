using System.Net;
using System.Text;

namespace Swgoh.Blazor.Clients;

public sealed class PlayerApiClient(HttpClient httpClient)
{
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
        CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder($"?page={page}&pageSize={pageSize}&type={Uri.EscapeDataString(type)}");
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Append("&search=").Append(Uri.EscapeDataString(search.Trim()));
        }

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

    public sealed record PlayerViewModel(
        long AllyCode,
        string PlayerId,
        string Name,
        string? GuildName,
        int Level,
        long GalacticPower,
        DateTimeOffset UpdatedAtUtc,
        int RosterCount);

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
        IReadOnlyCollection<RosterUnitViewModel> Items);

    public sealed record RosterUnitViewModel(
        string Id,
        string DefinitionId,
        string Name,
        IReadOnlyCollection<string> Factions,
        int Level,
        int Rarity,
        int GearTier,
        int RelicTier,
        long GalacticPower,
        bool IsShip,
        int ZetaCount,
        int OmicronCount);
}
