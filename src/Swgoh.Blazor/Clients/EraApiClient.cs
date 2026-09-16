namespace Swgoh.Blazor.Clients;

public sealed class EraApiClient(HttpClient httpClient)
{
    public async Task<EraAnalysisViewModel?> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/api/v1/eras/players/{allyCode}/current",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EraAnalysisViewModel>(cancellationToken);
    }

    public sealed record EraAnalysisViewModel(
        long AllyCode,
        string PlayerName,
        DateTimeOffset RosterUpdatedAtUtc,
        string CatalogVersion,
        string EraId,
        string EraName,
        DateOnly StartedOn,
        IReadOnlyCollection<EraUnitStatusViewModel> Units,
        EraJourneyProgressViewModel Journey,
        ColiseumAnalysisViewModel Coliseum,
        int OwnedUnits,
        int TotalUnits);

    public sealed record EraUnitStatusViewModel(
        string Key,
        string Name,
        string Alignment,
        string Role,
        IReadOnlyCollection<string> Categories,
        bool IsJourneyUnit,
        bool Owned,
        string? DefinitionId,
        string? ThumbnailName,
        int Stars,
        int RelicTier,
        long GalacticPower,
        string PrimarySynergy,
        string Notes);

    public sealed record EraJourneyProgressViewModel(
        string UnitName,
        IReadOnlyCollection<EraJourneyTierProgressViewModel> Tiers,
        int StarReadyTiers);

    public sealed record EraJourneyTierProgressViewModel(
        int Tier,
        int RequiredStars,
        bool StarRequirementsMet,
        IReadOnlyCollection<string> RequiredUnits,
        IReadOnlyCollection<string> MissingStarRequirements,
        IReadOnlyCollection<EraLevelRequirementViewModel> EraLevelRequirements,
        string RewardSummary);

    public sealed record EraLevelRequirementViewModel(string UnitName, int EraLevel);

    public sealed record ColiseumAnalysisViewModel(
        int MaxTier,
        int OwnedEraUnits,
        IReadOnlyCollection<ColiseumBossViewModel> Bosses,
        IReadOnlyCollection<ColiseumTierGuidanceViewModel> TierGuidance,
        IReadOnlyCollection<string> GeneralGuidance,
        bool EraLevelIsAvailableFromRoster);

    public sealed record ColiseumBossViewModel(
        string Id,
        string Name,
        string RotationNote,
        string StrategyNote);

    public sealed record ColiseumTierGuidanceViewModel(
        int Tier,
        int? RecommendedEraLevel,
        string Note);
}
