using System.Globalization;
using System.Text;

using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Eras;

public interface IEraService
{
    Task<EraAnalysis?> GetCurrentAsync(long allyCode, CancellationToken cancellationToken = default);
}

internal sealed class EraService(
    IPlayerProfileService playerProfileService,
    ISwgohGameDataCatalog gameDataCatalog) : IEraService
{
    public async Task<EraAnalysis?> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        Task<PlayerProfile?> playerTask = playerProfileService.GetAsync(allyCode, cancellationToken);
        Task<GameDataCatalog> gameDataTask = gameDataCatalog.GetAsync(cancellationToken);
        await Task.WhenAll(playerTask, gameDataTask).ConfigureAwait(false);

        PlayerProfile? player = await playerTask.ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        GameDataCatalog gameData = await gameDataTask.ConfigureAwait(false);
        EraUnitStatus[] units =
        [
            .. EraCatalog.Units.Select(definition => AnalyzeUnit(player, gameData, definition))
        ];
        EraJourneyProgress journey = AnalyzeJourney(units);
        ColiseumAnalysis coliseum = new(
            12,
            units.Count(unit => unit.Owned && !unit.IsJourneyUnit),
            [.. EraCatalog.ColiseumBosses.Select(boss => new ColiseumBoss(
                boss.Id,
                boss.Name,
                boss.RotationNote,
                boss.StrategyNote))],
            EraCatalog.ColiseumTierGuidance,
            [
                "El Coliseo solo permite unidades de la Era actual y Loaned Units; debes llevar al menos una unidad de Era propia.",
                "La puntuación es una high-water mark: usa primero una composición estable y después experimenta con líder, Loaned Units y Overcharge.",
                "El Era Level no forma parte del roster público que guarda actualmente la aplicación; por eso los requisitos EL se muestran separados de estrellas/reliquias.",
                "Las estrellas siguen siendo importantes porque bloquean progresión de Era Level: 4★ para EL66+, 5★ para EL76+, 6★ para EL86+ y 7★ para EL91+."
            ],
            EraLevelIsAvailableFromRoster: false);

        return new EraAnalysis(
            player.AllyCode,
            player.Name,
            player.UpdatedAtUtc,
            EraCatalog.Version,
            EraCatalog.EraId,
            EraCatalog.EraName,
            EraCatalog.StartedOn,
            units,
            journey,
            coliseum);
    }

    private static EraUnitStatus AnalyzeUnit(
        PlayerProfile player,
        GameDataCatalog gameData,
        EraUnitDefinition definition)
    {
        (RosterUnit Unit, GameUnitDefinition Definition)? match = player.Roster
            .Where(unit => !unit.IsShip)
            .Select(unit => gameData.Units.TryGetValue(unit.DefinitionId, out GameUnitDefinition? data)
                ? (Found: true, Unit: unit, Definition: data)
                : (Found: false, Unit: unit, Definition: (GameUnitDefinition?)null))
            .Where(item => item.Found && item.Definition is not null && Matches(definition, item.Unit, item.Definition))
            .OrderByDescending(item => item.Unit.Rarity)
            .ThenByDescending(item => item.Unit.RelicTier)
            .ThenByDescending(item => item.Unit.GalacticPower)
            .Select(item => ((RosterUnit Unit, GameUnitDefinition Definition)?)(item.Unit, item.Definition!))
            .FirstOrDefault();

        if (match is null)
        {
            return new EraUnitStatus(
                definition.Key,
                definition.Name,
                definition.Alignment,
                definition.Role,
                definition.Categories,
                definition.IsJourneyUnit,
                false,
                null,
                null,
                0,
                0,
                0,
                definition.PrimarySynergy,
                definition.Notes);
        }

        return new EraUnitStatus(
            definition.Key,
            definition.Name,
            definition.Alignment,
            definition.Role,
            definition.Categories,
            definition.IsJourneyUnit,
            true,
            match.Value.Unit.DefinitionId,
            match.Value.Definition.ThumbnailName,
            match.Value.Unit.Rarity,
            match.Value.Unit.RelicTier,
            match.Value.Unit.GalacticPower,
            definition.PrimarySynergy,
            definition.Notes);
    }

    private static EraJourneyProgress AnalyzeJourney(IReadOnlyCollection<EraUnitStatus> units)
    {
        Dictionary<string, EraUnitStatus> byName = units.ToDictionary(
            unit => Normalize(unit.Name),
            StringComparer.Ordinal);
        EraJourneyTierProgress[] tiers =
        [
            .. EraCatalog.DarthJarJarJourney.Select(tier =>
            {
                string[] missing =
                [
                    .. tier.RequiredUnits
                        .Select(name => (Name: name, Unit: byName.GetValueOrDefault(Normalize(name))))
                        .Where(item => item.Unit is null || !item.Unit.Owned || item.Unit.Stars < tier.RequiredStars)
                        .Select(item => item.Unit is null || !item.Unit.Owned
                            ? $"{item.Name}: no disponible"
                            : $"{item.Name}: {item.Unit.Stars}★ → {tier.RequiredStars}★")
                ];
                return new EraJourneyTierProgress(
                    tier.Tier,
                    tier.RequiredStars,
                    missing.Length == 0,
                    tier.RequiredUnits,
                    missing,
                    tier.EraLevelRequirements,
                    tier.RewardSummary);
            })
        ];

        return new EraJourneyProgress("Darth Jar Jar", tiers);
    }

    private static bool Matches(
        EraUnitDefinition target,
        RosterUnit unit,
        GameUnitDefinition definition)
    {
        string normalizedId = Normalize(unit.DefinitionId);
        string normalizedName = Normalize(definition.Name);
        return target.Aliases.Any(alias =>
        {
            string normalizedAlias = Normalize(alias);
            return string.Equals(normalizedName, normalizedAlias, StringComparison.Ordinal)
                || string.Equals(normalizedId, normalizedAlias, StringComparison.Ordinal)
                || normalizedId.Contains(normalizedAlias, StringComparison.Ordinal);
        });
    }

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}
