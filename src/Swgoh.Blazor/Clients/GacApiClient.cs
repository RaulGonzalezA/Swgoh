using System.Net;

namespace Swgoh.Blazor.Clients;

public sealed class GacApiClient(HttpClient httpClient)
{
    private const int LookupAttempts = 25;
    private const string WaitingMessage = "Gran Arena en espera. Todavía no hay un rival asignado.";
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);

    public async Task<CurrentGacOpponentResult> GetCurrentOpponentLookupAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < LookupAttempts; attempt++)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                $"/api/v1/gac/players/{allyCode}/current-opponent",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                CurrentGacUnavailableViewModel? pending =
                    await response.Content.ReadFromJsonAsync<CurrentGacUnavailableViewModel>(cancellationToken);
                if (pending?.Status == "Pending")
                {
                    await Task.Delay(PollDelay, cancellationToken);
                    continue;
                }

                return new CurrentGacOpponentResult(null, FriendlyUnavailableMessage(pending));
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            {
                CurrentGacUnavailableViewModel? unavailable =
                    await response.Content.ReadFromJsonAsync<CurrentGacUnavailableViewModel>(cancellationToken);
                if (unavailable?.Status == "Pending")
                {
                    await Task.Delay(PollDelay, cancellationToken);
                    continue;
                }

                return new CurrentGacOpponentResult(null, FriendlyUnavailableMessage(unavailable));
            }

            if (response.IsSuccessStatusCode)
            {
                CurrentGacOpponentViewModel? opponent =
                    await response.Content.ReadFromJsonAsync<CurrentGacOpponentViewModel>(cancellationToken);
                return opponent is null
                    ? new CurrentGacOpponentResult(null, WaitingMessage)
                    : new CurrentGacOpponentResult(opponent, null);
            }

            response.EnsureSuccessStatusCode();
        }

        return new CurrentGacOpponentResult(null, WaitingMessage);
    }

    public async Task<CurrentGacResult> GetCurrentOpponentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        CurrentGacOpponentResult lookup = await GetCurrentOpponentLookupAsync(allyCode, cancellationToken);
        if (lookup.Opponent is null)
        {
            return new CurrentGacResult(null, lookup.Message);
        }

        return await GetCurrentScoutingAsync(allyCode, cancellationToken);
    }

    public async Task<CurrentGacResult> GetCurrentScoutingAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < 4; attempt++)
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
                    await Task.Delay(PollDelay, cancellationToken);
                    continue;
                }

                return new CurrentGacResult(null, FriendlyUnavailableMessage(pending));
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            {
                CurrentGacUnavailableViewModel? unavailable =
                    await response.Content.ReadFromJsonAsync<CurrentGacUnavailableViewModel>(cancellationToken);
                return new CurrentGacResult(null, FriendlyUnavailableMessage(unavailable));
            }

            if (response.IsSuccessStatusCode)
            {
                CurrentGacScoutingViewModel? scouting =
                    await response.Content.ReadFromJsonAsync<CurrentGacScoutingViewModel>(cancellationToken);
                if (scouting?.Opponent is null)
                {
                    return new CurrentGacResult(null, WaitingMessage);
                }

                return new CurrentGacResult(scouting, null);
            }

            response.EnsureSuccessStatusCode();
        }

        return new CurrentGacResult(
            null,
            "Gran Arena en espera. El rival está localizado, pero el scouting sigue preparándose.");
    }

    private static string FriendlyUnavailableMessage(CurrentGacUnavailableViewModel? unavailable) =>
        unavailable?.Status switch
        {
            "NoActiveEvent" or "PlayerNotJoined" or "OpponentUnavailable" or "Pending" => WaitingMessage,
            _ => unavailable?.Message ?? WaitingMessage
        };

    public sealed record CurrentGacOpponentResult(CurrentGacOpponentViewModel? Opponent, string? Message);

    public sealed record CurrentGacResult(CurrentGacScoutingViewModel? Scouting, string? Message);

    public sealed record CurrentGacUnavailableViewModel(string Status, string? Message);

    public sealed record CurrentGacScoutingViewModel(
        CurrentGacOpponentViewModel Opponent,
        OpponentScoutingViewModel? Scouting,
        CurrentOpponentRosterScoutingViewModel? RosterScouting,
        CurrentGacBattlePlanViewModel? BattlePlan,
        IReadOnlyCollection<string>? Warnings = null)
    {
        public IReadOnlyCollection<string> DegradationWarnings => Warnings ?? [];
    }

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
