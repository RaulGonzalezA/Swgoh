using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class GacPersonalLearningContext
{
    private const int MaxAgeDays = 180;
    private readonly IReadOnlyCollection<GacPersonalRoundOutcome> rounds;
    private readonly DateTimeOffset now;

    private GacPersonalLearningContext(
        IReadOnlyCollection<GacPersonalRoundOutcome> rounds,
        DateTimeOffset now)
    {
        this.rounds = rounds;
        this.now = now;
    }

    public static GacPersonalLearningContext Empty { get; } = new([], DateTimeOffset.MinValue);

    public static GacPersonalLearningContext From(
        IReadOnlyCollection<GacPersonalRoundOutcome> rounds,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        return new GacPersonalLearningContext(
            [.. rounds.Where(round => now - round.UpdatedAtUtc <= TimeSpan.FromDays(MaxAgeDays))],
            now);
    }

    public GacPersonalLearningSignal Evaluate(
        GacVisibleDefenseDetails defense,
        GacTeamPresetDetails preset)
    {
        ArgumentNullException.ThrowIfNull(defense);
        ArgumentNullException.ThrowIfNull(preset);

        if (rounds.Count == 0 || defense.Squad.IsFleet != preset.Squad.IsFleet)
        {
            return GacPersonalLearningSignal.None;
        }

        string defenseSignature = Signature(defense.Squad);
        string attackSignature = Signature(preset.Squad);
        GacPersonalAttackOutcome[] exact =
        [
            .. rounds
                .Where(round => round.Format == preset.Format)
                .SelectMany(round => round.Attacks)
                .Where(outcome =>
                    Signature(outcome.DefenseSquad) == defenseSignature &&
                    Signature(outcome.AttackSquad) == attackSignature)
        ];
        if (exact.Length > 0)
        {
            return BuildSignal(exact, exactMatch: true);
        }

        GacPersonalAttackOutcome[] leaderPair =
        [
            .. rounds
                .Where(round => round.Format == preset.Format)
                .SelectMany(round => round.Attacks)
                .Where(outcome =>
                    string.Equals(
                        outcome.DefenseSquad.LeaderDefinitionId,
                        defense.Squad.Leader.DefinitionId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        outcome.AttackSquad.LeaderDefinitionId,
                        preset.Squad.Leader.DefinitionId,
                        StringComparison.OrdinalIgnoreCase) &&
                    outcome.DefenseSquad.IsFleet == defense.Squad.IsFleet &&
                    outcome.AttackSquad.IsFleet == preset.Squad.IsFleet)
        ];
        return leaderPair.Length >= 3
            ? BuildSignal(leaderPair, exactMatch: false)
            : GacPersonalLearningSignal.None;
    }

    private GacPersonalLearningSignal BuildSignal(
        IReadOnlyCollection<GacPersonalAttackOutcome> outcomes,
        bool exactMatch)
    {
        int samples = outcomes.Count;
        int wins = outcomes.Count(outcome => outcome.Status == GacAttackPlanStatus.Won);
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

        decimal winRate = samples == 0 ? 0m : wins / (decimal)samples;
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

    private static string Signature(GacPlannerSquadDetails squad) =>
        Signature(
            squad.Leader.DefinitionId,
            squad.Members.Select(unit => unit.DefinitionId),
            squad.IsFleet);

    private static string Signature(GacPlannerSquad squad) =>
        Signature(squad.LeaderDefinitionId, squad.MemberDefinitionIds, squad.IsFleet);

    private static string Signature(
        string leaderDefinitionId,
        IEnumerable<string> memberDefinitionIds,
        bool isFleet)
    {
        string[] units =
        [
            .. new[] { leaderDefinitionId }
                .Concat(memberDefinitionIds)
                .Select(id => id.Trim().ToUpperInvariant())
                .OrderBy(id => id, StringComparer.Ordinal)
        ];
        return $"{(isFleet ? "F" : "C")}:{string.Join('|', units)}";
    }
}
