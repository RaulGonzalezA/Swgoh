using System.Net;

namespace Swgoh.Blazor.Clients;

public sealed class GacApiClient(HttpClient httpClient)
{
    public async Task<CurrentGacResult> GetCurrentOpponentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < 210; attempt++)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                $"/api/v1/gac/players/{allyCode}/current-opponent/scouting",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                CurrentGacUnavailableViewModel? pending =
                    await response.Content.ReadFromJsonAsync<CurrentGacUnavailableViewModel>(cancellationToken);
                if (pending?.Status == "Pending")
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                return new CurrentGacResult(
                    null,
                    pending?.Message ?? "La búsqueda del rival continúa en segundo plano.");
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            {
                CurrentGacUnavailableViewModel? unavailable =
                    await response.Content.ReadFromJsonAsync<CurrentGacUnavailableViewModel>(cancellationToken);
                if (unavailable?.Status == "Pending")
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                return new CurrentGacResult(
                    null,
                    unavailable?.Message ?? "No hay un enfrentamiento de Gran Arena disponible.");
            }

            if (response.IsSuccessStatusCode)
            {
                CurrentGacScoutingViewModel? scouting =
                    await response.Content.ReadFromJsonAsync<CurrentGacScoutingViewModel>(cancellationToken);
                if (scouting?.Opponent is null)
                {
                    return new CurrentGacResult(
                        null,
                        "El enfrentamiento todavía no tiene datos completos. Vuelve a consultar en unos segundos.");
                }

                return new CurrentGacResult(scouting, null);
            }

            response.EnsureSuccessStatusCode();
            return new CurrentGacResult(null, "No se ha podido consultar la Gran Arena.");
        }

        return new CurrentGacResult(
            null,
            "La búsqueda continúa en segundo plano. Puedes volver a consultar más tarde.");
    }

    public sealed record CurrentGacResult(CurrentGacScoutingViewModel? Scouting, string? Message);

    public sealed record CurrentGacUnavailableViewModel(string Status, string? Message);

    public sealed record CurrentGacScoutingViewModel(
        CurrentGacOpponentViewModel Opponent,
        OpponentScoutingViewModel? Scouting,
        CurrentOpponentRosterScoutingViewModel? RosterScouting,
        CurrentGacBattlePlanViewModel? BattlePlan);

    public sealed record CurrentGacOpponentViewModel(
        long PlayerAllyCode,
        long OpponentAllyCode,
        string OpponentName,
        string League,
        string Format,
        int? RoundNumber);

    public sealed record OpponentScoutingViewModel(
        int RoundsAnalyzed,
        int SeasonsAnalyzed,
        decimal? FullClearRate,
        decimal? AverageFirstAttackDelayMinutes);

    public sealed record CurrentOpponentRosterScoutingViewModel(
        PlayerApiClient.PlayerRosterAnalysisViewModel Analysis,
        IReadOnlyCollection<BattleUnitViewModel> GalacticLegends,
        IReadOnlyCollection<BattleUnitViewModel> TopCharacters,
        IReadOnlyCollection<BattleUnitViewModel> TopShips,
        IReadOnlyCollection<BattleUnitViewModel> OmicronCharacters);

    public sealed record CurrentGacBattlePlanViewModel(
        RosterComparisonViewModel Comparison,
        IReadOnlyCollection<BattleThreatViewModel> Threats,
        IReadOnlyCollection<DefensePredictionViewModel> DefensePredictions,
        IReadOnlyCollection<AttackReserveViewModel> AttackReserves,
        IReadOnlyCollection<CounterSuggestionViewModel> CounterSuggestions,
        IReadOnlyCollection<string> Warnings);

    public sealed record RosterComparisonViewModel(
        long PlayerGalacticPower,
        long OpponentGalacticPower,
        long GalacticPowerDelta,
        int PlayerGalacticLegends,
        int OpponentGalacticLegends,
        int PlayerOmicronCharacters,
        int OpponentOmicronCharacters,
        int PlayerRelic7Plus,
        int OpponentRelic7Plus,
        int PlayerRelic9Plus,
        int OpponentRelic9Plus);

    public sealed record BattleUnitViewModel(
        string DefinitionId,
        string Name,
        long GalacticPower,
        int RelicTier,
        int ZetaCount,
        int OmicronCount,
        bool IsShip,
        bool IsGalacticLegend);

    public sealed record BattleThreatViewModel(
        BattleUnitViewModel Unit,
        int Score,
        string Priority,
        string Category,
        string Reason);

    public sealed record DefensePredictionViewModel(
        string LeaderName,
        string LeaderDefinitionId,
        IReadOnlyCollection<string> MemberNames,
        bool IsFleet,
        decimal? Probability,
        string Confidence,
        string Source,
        string? SquadDefinitionName,
        string? VariantName);

    public sealed record AttackReserveViewModel(
        BattleUnitViewModel Unit,
        string Role,
        string Priority,
        string Reason);

    public sealed record CounterSuggestionViewModel(
        BattleUnitViewModel Threat,
        IReadOnlyCollection<BattleUnitViewModel> CandidateAnchors,
        string Confidence,
        string Source,
        string Rationale,
        bool RequiresDatacronVerification,
        IReadOnlyCollection<BattleUnitViewModel>? RecommendedTeam,
        int? Uses,
        decimal? WinRate,
        decimal? OneShotRate,
        decimal? AverageBanners,
        int? PlayersObserved);
}
