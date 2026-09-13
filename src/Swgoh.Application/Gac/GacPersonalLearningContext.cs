using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class GacPersonalLearningContext
{
    private readonly IReadOnlyCollection<GacPersonalMatchupStatistics> statistics;

    private GacPersonalLearningContext(IReadOnlyCollection<GacPersonalMatchupStatistics> statistics)
    {
        this.statistics = statistics;
    }

    public static GacPersonalLearningContext Empty { get; } = new([]);

    public static GacPersonalLearningContext From(
        IReadOnlyCollection<GacPersonalMatchupStatistics> statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        return new GacPersonalLearningContext(statistics);
    }

    public GacPersonalLearningSignal Evaluate(
        GacVisibleDefenseDetails defense,
        GacTeamPresetDetails preset)
    {
        ArgumentNullException.ThrowIfNull(defense);
        ArgumentNullException.ThrowIfNull(preset);

        if (statistics.Count == 0 || defense.Squad.IsFleet != preset.Squad.IsFleet)
        {
            return GacPersonalLearningSignal.None;
        }

        string exactKey = GacPersonalBattleObservation.BuildMatchupKey(
            preset.Format,
            preset.Squad.IsFleet,
            preset.Squad.AllUnits.Select(unit => unit.DefinitionId),
            defense.Squad.AllUnits.Select(unit => unit.DefinitionId));
        GacPersonalMatchupStatistics? exact = statistics.FirstOrDefault(item =>
            string.Equals(item.MatchupKey, exactKey, StringComparison.Ordinal));
        if (exact is not null && exact.Uses > 0)
        {
            return BuildSignal(exact.Uses, exact.Wins, exactMatch: true);
        }

        string attackerLeader = preset.Squad.Leader.DefinitionId;
        string defenderLeader = defense.Squad.Leader.DefinitionId;
        GacPersonalMatchupStatistics[] leaderPair =
        [
            .. statistics.Where(item =>
                item.IsFleet == preset.Squad.IsFleet &&
                item.AttackerDefinitionIds.Count > 0 &&
                item.DefenderDefinitionIds.Count > 0 &&
                string.Equals(
                    item.AttackerDefinitionIds.First(),
                    attackerLeader,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    item.DefenderDefinitionIds.First(),
                    defenderLeader,
                    StringComparison.OrdinalIgnoreCase))
        ];
        int samples = leaderPair.Sum(item => item.Uses);
        if (samples < 3)
        {
            return GacPersonalLearningSignal.None;
        }

        return BuildSignal(samples, leaderPair.Sum(item => item.Wins), exactMatch: false);
    }

    private static GacPersonalLearningSignal BuildSignal(int samples, int wins, bool exactMatch)
    {
        int failures = samples - wins;
        decimal posterior;
        decimal reliability;
        decimal adjustment;
        string scope;

        if (exactMatch)
        {
            posterior = (wins + 2m) / (samples + 4m);
            reliability = Math.Min(1m, samples / 8m);
            adjustment = Math.Clamp((posterior - 0.5m) * 12m * reliability, -6m, 6m);
            scope = "Exact";
        }
        else
        {
            posterior = (wins + 3m) / (samples + 6m);
            reliability = Math.Min(1m, samples / 12m);
            adjustment = Math.Clamp((posterior - 0.5m) * 6m * reliability, -3m, 3m);
            scope = "LeaderPair";
        }

        decimal winRate = wins / (decimal)samples;
        decimal roundedAdjustment = Math.Round(adjustment, 1);
        string label = exactMatch ? "Tu histórico exacto" : "Tu tendencia por líderes";
        string summary = $"{label}: {wins}/{samples} victorias ({winRate:P0}); ajuste {roundedAdjustment:+0.#;-0.#;0}.";

        return new GacPersonalLearningSignal(
            roundedAdjustment,
            samples,
            wins,
            failures,
            Math.Round(winRate, 3),
            scope,
            summary);
    }
}
