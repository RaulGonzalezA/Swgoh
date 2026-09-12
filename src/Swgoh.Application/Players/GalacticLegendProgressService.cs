using Swgoh.Application.GameData;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

internal sealed class GalacticLegendProgressService(
    IPlayerRepository repository,
    ISwgohGameDataCatalog gameDataCatalog) : IGalacticLegendProgressService
{
    public async Task<IReadOnlyCollection<GalacticLegendProgress>?> GetAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        PlayerProfile? player = await repository.FindByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        GameDataCatalog catalog = await gameDataCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, RosterUnit> roster = player.Roster
            .GroupBy(unit => unit.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return
        [
            .. catalog.GalacticLegends
                .OrderBy(legend => legend.UnitBaseId, StringComparer.Ordinal)
                .Select(legend => BuildProgress(legend, roster))
        ];
    }

    private static GalacticLegendProgress BuildProgress(
        GalacticLegendDefinition legend,
        IReadOnlyDictionary<string, RosterUnit> roster)
    {
        bool unlocked = roster.ContainsKey(legend.UnitBaseId);
        GalacticLegendRequirementProgress[] requirements =
        [
            .. legend.Requirements
                .OrderBy(requirement => requirement.UnitBaseId, StringComparer.Ordinal)
                .Select(requirement => BuildRequirementProgress(requirement, roster))
        ];

        int completed = requirements.Count(requirement => requirement.Complete);
        decimal percentage = requirements.Length == 0
            ? unlocked ? 100m : 0m
            : Math.Round(completed * 100m / requirements.Length, 1, MidpointRounding.AwayFromZero);

        return new GalacticLegendProgress(
            legend.UnitBaseId,
            unlocked,
            completed,
            requirements.Length,
            percentage,
            requirements);
    }

    private static GalacticLegendRequirementProgress BuildRequirementProgress(
        GalacticLegendUnitRequirement requirement,
        IReadOnlyDictionary<string, RosterUnit> roster)
    {
        bool owned = roster.TryGetValue(requirement.UnitBaseId, out RosterUnit? unit);
        int rarity = unit?.Rarity ?? 0;
        int gearTier = unit?.GearTier ?? 0;
        int relicTier = unit?.RelicTier ?? 0;
        bool complete = owned
            && rarity >= requirement.MinimumRarity
            && gearTier >= requirement.MinimumGearTier
            && relicTier >= requirement.MinimumRelicTier;

        return new GalacticLegendRequirementProgress(
            requirement.UnitBaseId,
            owned,
            rarity,
            gearTier,
            relicTier,
            requirement.MinimumRarity,
            requirement.MinimumGearTier,
            requirement.MinimumRelicTier,
            complete);
    }
}
