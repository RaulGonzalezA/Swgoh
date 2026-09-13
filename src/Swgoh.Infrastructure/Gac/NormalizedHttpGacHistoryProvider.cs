using System.Net.Http.Json;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;

namespace Swgoh.Infrastructure.Gac;

internal sealed class NormalizedHttpGacHistoryProvider(
    IHttpClientFactory httpClientFactory,
    string? apiKey) : IGacHistoryProvider
{
    internal const string HttpClientName = "gac-history-provider";

    public string Name => "NormalizedHttp";
    public bool IsEnabled => true;

    public async Task<IReadOnlyCollection<GacHistoryRoundInput>> GetAsync(
        long allyCode,
        GacFormat format,
        int maxRounds,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"gac/players/{allyCode}/history?format={FormatName(format)}&maxRounds={maxRounds}");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        }

        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        HistoryEnvelope? envelope = await response.Content
            .ReadFromJsonAsync<HistoryEnvelope>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (envelope?.Rounds is null || envelope.Rounds.Count == 0)
        {
            return [];
        }

        return [.. envelope.Rounds.Select(MapRound)];
    }

    private static GacHistoryRoundInput MapRound(HistoryRound round) => new(
        round.Season,
        round.EventNumber,
        round.RoundNumber,
        ParseFormat(round.Format),
        ParseLeague(round.League),
        round.StartedAtUtc,
        round.FullClear,
        string.IsNullOrWhiteSpace(round.Source) ? "normalized-http" : round.Source,
        [.. (round.Defenses ?? []).Select(defense => new GacHistoryDefenseInput(
            defense.Zone,
            MapSquad(defense.Squad),
            defense.Holds,
            defense.Defeated))],
        [.. (round.OffenseBattles ?? []).Select(battle => new GacHistoryOffenseBattleInput(
            battle.Zone,
            MapSquad(battle.Defender),
            MapSquad(battle.Attacker),
            battle.Won,
            battle.Banners,
            battle.Attempt,
            battle.AttackedAtUtc))]);

    private static GacHistorySquadInput MapSquad(HistorySquad squad) => new(
        squad.LeaderDefinitionId,
        squad.MemberDefinitionIds ?? [],
        squad.IsFleet);

    private static GacFormat ParseFormat(string value) => value.Trim().ToLowerInvariant() switch
    {
        "3" or "3v3" or "threevsthree" => GacFormat.ThreeVsThree,
        "5" or "5v5" or "fivevsfive" => GacFormat.FiveVsFive,
        _ => throw new InvalidOperationException($"History provider returned unsupported GAC format '{value}'.")
    };

    private static GacLeague ParseLeague(string value) =>
        Enum.TryParse(value.Trim(), ignoreCase: true, out GacLeague league) && Enum.IsDefined(league)
            ? league
            : throw new InvalidOperationException($"History provider returned unsupported GAC league '{value}'.");

    private static string FormatName(GacFormat format) => format switch
    {
        GacFormat.ThreeVsThree => "3v3",
        GacFormat.FiveVsFive => "5v5",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GAC format.")
    };

    private sealed record HistoryEnvelope(IReadOnlyCollection<HistoryRound>? Rounds);

    private sealed record HistoryRound(
        int Season,
        int EventNumber,
        int RoundNumber,
        string Format,
        string League,
        DateTimeOffset StartedAtUtc,
        bool? FullClear,
        string? Source,
        IReadOnlyCollection<HistoryDefense>? Defenses,
        IReadOnlyCollection<HistoryOffenseBattle>? OffenseBattles);

    private sealed record HistoryDefense(
        string Zone,
        HistorySquad Squad,
        int Holds,
        bool Defeated);

    private sealed record HistoryOffenseBattle(
        string Zone,
        HistorySquad Defender,
        HistorySquad Attacker,
        bool Won,
        int Banners,
        int Attempt,
        DateTimeOffset? AttackedAtUtc);

    private sealed record HistorySquad(
        string LeaderDefinitionId,
        IReadOnlyCollection<string>? MemberDefinitionIds,
        bool IsFleet);
}
