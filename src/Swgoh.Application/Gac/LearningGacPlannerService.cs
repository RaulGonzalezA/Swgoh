using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed class LearningGacPlannerService(
    GacPlannerService inner,
    IGacPersonalLearningService personalLearningService) : IGacPlannerService
{
    public async Task<GacPlannerLookup> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        GacPlannerLookup lookup = await inner.GetCurrentAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return await EnrichAsync(allyCode, lookup, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GacPlannerLookup> SaveCurrentAsync(
        long allyCode,
        SaveCurrentGacRoundPlan input,
        CancellationToken cancellationToken = default)
    {
        GacPlannerLookup lookup = await inner
            .SaveCurrentAsync(allyCode, input, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.State is not null)
        {
            await personalLearningService
                .SyncPlannerStateAsync(lookup.State, cancellationToken)
                .ConfigureAwait(false);
        }

        return await EnrichAsync(allyCode, lookup, cancellationToken).ConfigureAwait(false);
    }

    public Task<GacTeamPresetDetails> CreatePresetAsync(
        long allyCode,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default) =>
        inner.CreatePresetAsync(allyCode, input, cancellationToken);

    public Task<GacTeamPresetDetails?> UpdatePresetAsync(
        long allyCode,
        Guid id,
        SaveGacTeamPreset input,
        CancellationToken cancellationToken = default) =>
        inner.UpdatePresetAsync(allyCode, id, input, cancellationToken);

    public Task<bool> DeletePresetAsync(
        long allyCode,
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.DeletePresetAsync(allyCode, id, cancellationToken);

    private async Task<GacPlannerLookup> EnrichAsync(
        long allyCode,
        GacPlannerLookup lookup,
        CancellationToken cancellationToken)
    {
        if (lookup.State is null)
        {
            return lookup;
        }

        GacPlannerState state = lookup.State;
        IReadOnlyCollection<GacPersonalMatchupStatistics> statistics = await personalLearningService
            .GetStatisticsAsync(allyCode, state.Plan.Format, cancellationToken)
            .ConfigureAwait(false);
        if (statistics.Count == 0)
        {
            return lookup;
        }

        var context = GacPersonalLearningContext.From(statistics);
        Dictionary<Guid, GacPlannerCounterHint> existingHints = state.Plan.CounterHints
            .ToDictionary(item => item.DefenseId);
        var personalizedHints = new List<GacPlannerCounterHint>();

        foreach (GacVisibleDefenseDetails defense in state.Plan.VisibleDefenses)
        {
            existingHints.TryGetValue(defense.Id, out GacPlannerCounterHint? existing);
            GacPlannerCounterHint? personalizedExisting = PersonalizeExistingHint(
                existing,
                defense,
                state.Presets,
                context);
            PersonalCandidate? bestPersonal = FindBestPersonalCandidate(defense, state.Presets, context);
            GacPlannerCounterHint? personalHint = bestPersonal is null
                ? null
                : BuildPersonalHint(defense, bestPersonal);

            GacPlannerCounterHint? selected = SelectHint(personalizedExisting, personalHint);
            if (selected is not null)
            {
                personalizedHints.Add(selected);
            }
        }

        GacRoundPlanDetails plan = state.Plan with { CounterHints = personalizedHints };
        return lookup with { State = state with { Plan = plan } };
    }

    private static GacPlannerCounterHint? PersonalizeExistingHint(
        GacPlannerCounterHint? hint,
        GacVisibleDefenseDetails defense,
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        GacPersonalLearningContext context)
    {
        if (hint is null || hint.MatchingTeamPresetId is not Guid presetId)
        {
            return hint;
        }

        GacTeamPresetDetails? preset = presets.FirstOrDefault(item => item.Id == presetId);
        if (preset is null)
        {
            return hint;
        }

        GacPersonalLearningSignal signal = context.Evaluate(defense, preset);
        if (signal.Samples == 0)
        {
            return hint;
        }

        return hint with
        {
            Confidence = AdjustConfidence(hint.Confidence, signal),
            Source = hint.Source.Contains("Personal", StringComparison.OrdinalIgnoreCase)
                ? hint.Source
                : $"{hint.Source}+Personal",
            Rationale = $"{hint.Rationale} {signal.Summary}",
            WinRate = CalibrateRate(hint.WinRate, signal.Adjustment),
            PersonalUses = signal.Samples,
            PersonalWinRate = signal.WinRate,
            PersonalAdjustment = signal.Adjustment,
            PersonalScope = signal.Scope
        };
    }

    private static PersonalCandidate? FindBestPersonalCandidate(
        GacVisibleDefenseDetails defense,
        IReadOnlyCollection<GacTeamPresetDetails> presets,
        GacPersonalLearningContext context)
    {
        return presets
            .Where(preset =>
                preset.Use != GacPlannerTeamUse.Defense &&
                preset.Squad.IsFleet == defense.Squad.IsFleet)
            .Select(preset => new PersonalCandidate(preset, context.Evaluate(defense, preset)))
            .Where(candidate => IsStrongEnough(candidate.Signal))
            .OrderByDescending(candidate => candidate.Signal.Adjustment)
            .ThenByDescending(candidate => candidate.Signal.Samples)
            .ThenByDescending(candidate => candidate.Signal.WinRate ?? 0m)
            .FirstOrDefault();
    }

    private static GacPlannerCounterHint BuildPersonalHint(
        GacVisibleDefenseDetails defense,
        PersonalCandidate candidate)
    {
        GacPersonalLearningSignal signal = candidate.Signal;
        decimal conservativeRate = ConservativeRate(signal);
        string confidence = signal.Scope == "Exact" && signal.Samples >= 8 && signal.WinRate >= 0.75m
            ? "High"
            : "Medium";
        return new GacPlannerCounterHint(
            defense.Id,
            defense.Squad.Leader.Name,
            confidence,
            "PersonalBattleHistory",
            $"{signal.Summary} Se usa como señal personal acotada y no sustituye un histórico global más fuerte.",
            RequiresDatacronVerification: false,
            candidate.Preset.Id,
            candidate.Preset.Squad.AllUnits,
            signal.Samples,
            conservativeRate,
            OneShotRate: null,
            AverageBanners: null,
            PlayersObserved: 1,
            signal.Samples,
            signal.WinRate,
            signal.Adjustment,
            signal.Scope);
    }

    private static GacPlannerCounterHint? SelectHint(
        GacPlannerCounterHint? existing,
        GacPlannerCounterHint? personal)
    {
        if (existing is null)
        {
            return personal;
        }

        if (personal is null || personal.MatchingTeamPresetId == existing.MatchingTeamPresetId)
        {
            return existing;
        }

        decimal existingScore = EstimateHintStrength(existing);
        decimal personalScore = EstimateHintStrength(personal);
        bool existingIsHeuristic = existing.Source.Contains("Heuristic", StringComparison.OrdinalIgnoreCase) ||
            existing.Source.Contains("RosterStrength", StringComparison.OrdinalIgnoreCase);
        return personalScore > existingScore + 1.5m ||
               (existingIsHeuristic && personalScore >= existingScore - 2m)
            ? personal
            : existing;
    }

    private static bool IsStrongEnough(GacPersonalLearningSignal signal) =>
        signal.Adjustment >= 0.8m &&
        (signal.Scope == "Exact" ? signal.Samples >= 3 : signal.Samples >= 6);

    private static string AdjustConfidence(string confidence, GacPersonalLearningSignal signal)
    {
        if (signal.Samples < 3 || Math.Abs(signal.Adjustment) < 1.5m)
        {
            return confidence;
        }

        if (signal.Adjustment > 0m)
        {
            return confidence switch
            {
                "Low" => "Medium",
                "Medium" when signal.Samples >= 6 => "High",
                _ => confidence
            };
        }

        return confidence switch
        {
            "High" => "Medium",
            "Medium" => "Low",
            _ => confidence
        };
    }

    private static decimal? CalibrateRate(decimal? value, decimal adjustment)
    {
        if (value is not decimal current || adjustment == 0m)
        {
            return value;
        }

        bool percentage = current > 1m;
        decimal normalized = percentage ? current / 100m : current;
        decimal calibrated = Math.Clamp(normalized + (adjustment / 28m), 0m, 1m);
        return Math.Round(percentage ? calibrated * 100m : calibrated, 4);
    }

    private static decimal ConservativeRate(GacPersonalLearningSignal signal)
    {
        decimal prior = signal.Scope == "Exact" ? 2m : 3m;
        decimal rate = (signal.Wins + prior) / (signal.Samples + (prior * 2m));
        return Math.Round(rate, 4);
    }

    private static decimal EstimateHintStrength(GacPlannerCounterHint hint)
    {
        decimal score = hint.Confidence switch
        {
            "High" => 78m,
            "Medium" => 70m,
            _ => 62m
        };
        if (hint.WinRate is decimal winRate)
        {
            score = 60m + (NormalizeRate(winRate) * 28m);
        }

        if (hint.OneShotRate is decimal oneShotRate)
        {
            score += NormalizeRate(oneShotRate) * 6m;
        }

        if (hint.Uses is int uses)
        {
            score += Math.Min(6m, (decimal)Math.Log10(Math.Max(1, uses)) * 3m);
        }

        if (hint.Source.Contains("RosterStrengthHeuristic", StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Min(score, 66m);
        }

        return Math.Clamp(score, 0m, 98m);
    }

    private static decimal NormalizeRate(decimal value) =>
        Math.Clamp(value <= 1m ? value : value / 100m, 0m, 1m);

    private sealed record PersonalCandidate(
        GacTeamPresetDetails Preset,
        GacPersonalLearningSignal Signal);
}
