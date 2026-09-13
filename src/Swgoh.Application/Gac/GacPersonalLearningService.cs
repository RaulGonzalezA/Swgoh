using Swgoh.Application.Abstractions;
using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

public interface IGacPersonalLearningService
{
    Task SyncPlannerStateAsync(
        GacPlannerState state,
        CancellationToken cancellationToken = default);

    Task SyncRoundPlanAsync(
        GacRoundPlan plan,
        IReadOnlyDictionary<Guid, GacTeamPreset> presets,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<GacPersonalMatchupStatistics>> GetStatisticsAsync(
        long playerAllyCode,
        GacFormat format,
        CancellationToken cancellationToken = default);
}

public sealed record GacPersonalMatchupStatistics(
    string MatchupKey,
    bool IsFleet,
    IReadOnlyCollection<string> AttackerDefinitionIds,
    IReadOnlyCollection<string> DefenderDefinitionIds,
    int Uses,
    int Wins,
    decimal WinRate,
    decimal? OneShotRate,
    decimal? AverageBanners,
    DateTimeOffset LastSeenAtUtc);

internal sealed class GacPersonalLearningService(
    IGacPersonalBattleRepository repository,
    IClock clock) : IGacPersonalLearningService
{
    private static readonly TimeSpan LearningWindow = TimeSpan.FromDays(180);

    public async Task SyncPlannerStateAsync(
        GacPlannerState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        Dictionary<Guid, GacVisibleDefenseDetails> defenses = state.Plan.VisibleDefenses.ToDictionary(item => item.Id);
        IReadOnlyCollection<GacPersonalBattleObservation> existing = await repository
            .GetRoundAsync(
                state.Plan.PlayerAllyCode,
                state.Plan.EventInstanceId,
                state.Plan.RoundNumber,
                cancellationToken)
            .ConfigureAwait(false);
        var expectedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (GacAttackAssignmentDetails attack in state.Plan.Attacks)
        {
            if (attack.Status is not (GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed) ||
                !defenses.TryGetValue(attack.DefenseId, out GacVisibleDefenseDetails? defense))
            {
                continue;
            }

            string observationId = GacPersonalBattleObservation.BuildId(
                state.Plan.PlayerAllyCode,
                state.Plan.EventInstanceId,
                state.Plan.RoundNumber,
                attack.Id);
            expectedIds.Add(observationId);
            GacPersonalBattleObservation? previous = existing.FirstOrDefault(item =>
                string.Equals(item.Id, observationId, StringComparison.Ordinal));

            GacPersonalBattleObservation observation = GacPersonalBattleObservation.Create(
                state.Plan.PlayerAllyCode,
                state.Plan.OpponentAllyCode,
                state.Plan.EventInstanceId,
                state.Plan.RoundNumber,
                state.Plan.Format,
                attack.Id,
                attack.DefenseId,
                attack.Attempt,
                defense.Squad.IsFleet,
                attack.Team.Squad.AllUnits.Select(unit => unit.DefinitionId),
                defense.Squad.AllUnits.Select(unit => unit.DefinitionId),
                attack.Status == GacAttackPlanStatus.Won,
                previous?.Banners,
                clock.UtcNow);
            await repository.UpsertAsync(observation, cancellationToken).ConfigureAwait(false);
        }

        await DeleteStaleAsync(existing, expectedIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task SyncRoundPlanAsync(
        GacRoundPlan plan,
        IReadOnlyDictionary<Guid, GacTeamPreset> presets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(presets);

        Dictionary<Guid, GacVisibleDefense> defenses = plan.VisibleDefenses.ToDictionary(item => item.Id);
        IReadOnlyCollection<GacPersonalBattleObservation> existing = await repository
            .GetRoundAsync(plan.PlayerAllyCode, plan.EventInstanceId, plan.RoundNumber, cancellationToken)
            .ConfigureAwait(false);
        var expectedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (GacAttackAssignment attack in plan.Attacks)
        {
            if (attack.Status is not (GacAttackPlanStatus.Won or GacAttackPlanStatus.Failed) ||
                !defenses.TryGetValue(attack.DefenseId, out GacVisibleDefense? defense) ||
                !presets.TryGetValue(attack.TeamPresetId, out GacTeamPreset? preset))
            {
                continue;
            }

            string observationId = GacPersonalBattleObservation.BuildId(
                plan.PlayerAllyCode,
                plan.EventInstanceId,
                plan.RoundNumber,
                attack.Id);
            expectedIds.Add(observationId);
            GacPersonalBattleObservation? previous = existing.FirstOrDefault(item =>
                string.Equals(item.Id, observationId, StringComparison.Ordinal));

            GacPersonalBattleObservation observation = GacPersonalBattleObservation.Create(
                plan.PlayerAllyCode,
                plan.OpponentAllyCode,
                plan.EventInstanceId,
                plan.RoundNumber,
                plan.Format,
                attack.Id,
                attack.DefenseId,
                attack.Attempt,
                defense.Squad.IsFleet,
                preset.Squad.AllUnitDefinitionIds,
                defense.Squad.AllUnitDefinitionIds,
                attack.Status == GacAttackPlanStatus.Won,
                previous?.Banners,
                clock.UtcNow);
            await repository.UpsertAsync(observation, cancellationToken).ConfigureAwait(false);
        }

        await DeleteStaleAsync(existing, expectedIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<GacPersonalMatchupStatistics>> GetStatisticsAsync(
        long playerAllyCode,
        GacFormat format,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<GacPersonalBattleObservation> observations = await repository
            .GetAsync(playerAllyCode, format, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset cutoff = clock.UtcNow - LearningWindow;

        return
        [
            .. observations
                .Where(item => item.RecordedAtUtc >= cutoff)
                .GroupBy(item => item.MatchupKey, StringComparer.Ordinal)
                .Select(group => ToStatistics(group.Key, group))
                .OrderByDescending(item => item.LastSeenAtUtc)
        ];
    }

    private async Task DeleteStaleAsync(
        IReadOnlyCollection<GacPersonalBattleObservation> existing,
        IReadOnlySet<string> expectedIds,
        CancellationToken cancellationToken)
    {
        foreach (GacPersonalBattleObservation stale in existing.Where(item => !expectedIds.Contains(item.Id)))
        {
            await repository.DeleteAsync(stale.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private static GacPersonalMatchupStatistics ToStatistics(
        string matchupKey,
        IEnumerable<GacPersonalBattleObservation> source)
    {
        GacPersonalBattleObservation[] observations = [.. source];
        GacPersonalBattleObservation sample = observations[0];
        int uses = observations.Length;
        int wins = observations.Count(item => item.Won);
        GacPersonalBattleObservation[] firstAttempts = [.. observations.Where(item => item.Attempt == 1)];
        int[] bannerValues =
        [
            .. observations
                .Where(item => item.Won && item.Banners is not null)
                .Select(item => item.Banners!.Value)
        ];

        return new GacPersonalMatchupStatistics(
            matchupKey,
            sample.IsFleet,
            sample.AttackerDefinitionIds,
            sample.DefenderDefinitionIds,
            uses,
            wins,
            uses == 0 ? 0m : Math.Round(wins / (decimal)uses, 4),
            firstAttempts.Length == 0
                ? null
                : Math.Round(firstAttempts.Count(item => item.Won) / (decimal)firstAttempts.Length, 4),
            bannerValues.Length == 0 ? null : Math.Round((decimal)bannerValues.Average(), 1),
            observations.Max(item => item.RecordedAtUtc));
    }
}
