using Swgoh.Domain.Gac;

namespace Swgoh.Application.Gac;

internal sealed record GacAttackPresetMaterialization(
    IReadOnlyDictionary<Guid, Guid> IdMap,
    IReadOnlyCollection<Guid> CreatedPresetIds);

internal static class GacAttackGeneratedPresetMaterializer
{
    public static async Task<GacAttackPresetMaterialization> MaterializeAsync(
        IGacPlannerService plannerService,
        long allyCode,
        GacFormat format,
        GacAttackOptimizationResult optimization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plannerService);
        ArgumentNullException.ThrowIfNull(optimization);

        HashSet<Guid> selectedIds = optimization.Recommendations
            .Select(recommendation => recommendation.TeamPresetId)
            .ToHashSet();
        var idMap = new Dictionary<Guid, Guid>();
        var created = new List<Guid>();

        try
        {
            foreach (GacTeamPresetDetails candidate in optimization.GeneratedTeams
                         .Where(candidate => selectedIds.Contains(candidate.Id)))
            {
                GacTeamPresetDetails persisted = await plannerService.CreatePresetAsync(
                    allyCode,
                    new SaveGacTeamPreset(
                        candidate.Name,
                        format,
                        candidate.Use == GacPlannerTeamUse.Defense
                            ? GacPlannerTeamUse.Offense
                            : candidate.Use,
                        candidate.Squad.Leader.DefinitionId,
                        [.. candidate.Squad.Members.Select(unit => unit.DefinitionId)],
                        candidate.Squad.IsFleet),
                    cancellationToken).ConfigureAwait(false);
                idMap[candidate.Id] = persisted.Id;
                created.Add(persisted.Id);
            }

            return new GacAttackPresetMaterialization(idMap, created);
        }
        catch
        {
            await GacRosterDefenseCandidateService
                .RollbackMaterializationAsync(plannerService, allyCode, created)
                .ConfigureAwait(false);
            throw;
        }
    }

    public static GacAttackOptimizationResult Remap(
        GacAttackOptimizationResult optimization,
        IReadOnlyDictionary<Guid, Guid> idMap)
    {
        ArgumentNullException.ThrowIfNull(optimization);
        ArgumentNullException.ThrowIfNull(idMap);
        if (idMap.Count == 0)
        {
            return optimization;
        }

        GacAttackOptimizationRecommendation[] recommendations =
        [
            .. optimization.Recommendations.Select(recommendation =>
                idMap.TryGetValue(recommendation.TeamPresetId, out Guid persistedId)
                    ? recommendation with { TeamPresetId = persistedId }
                    : recommendation)
        ];
        GacCounterDefenseAnalysis[] analyses =
        [
            .. optimization.CounterEngine.Select(analysis => analysis with
            {
                Candidates =
                [
                    .. analysis.Candidates.Select(candidate =>
                        idMap.TryGetValue(candidate.TeamPresetId, out Guid persistedId)
                            ? candidate with { TeamPresetId = persistedId }
                            : candidate)
                ]
            })
        ];
        return optimization with
        {
            Recommendations = recommendations,
            CounterAnalyses = analyses,
            GeneratedTeamPresets = []
        };
    }
}
