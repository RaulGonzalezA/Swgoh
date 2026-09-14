using Swgoh.Application.GameData;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

internal sealed class PlayerRosterService(
    IPlayerRepository repository,
    ISwgohGameDataCatalog gameDataCatalog,
    IRosterGameDataCatalog? rosterGameDataCatalog = null,
    PlayerRosterSnapshotCache? snapshotCache = null) : IPlayerRosterService
{
    private const int MaxPageSize = 100;
    private readonly PlayerRosterSnapshotCache cache = snapshotCache ?? new PlayerRosterSnapshotCache();

    public async Task<PlayerRosterPage?> GetAsync(
        long allyCode,
        PlayerRosterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);

        PlayerRosterSnapshot? snapshot = await GetSnapshotAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        IEnumerable<PlayerRosterUnit> units = snapshot.Units;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.Trim();
            units = units.Where(unit => MatchesSearch(unit, search));
        }

        units = query.Type switch
        {
            PlayerRosterUnitType.Character => units.Where(unit => !unit.IsShip),
            PlayerRosterUnitType.Ship => units.Where(unit => unit.IsShip),
            _ => units
        };

        if (!string.IsNullOrWhiteSpace(query.Faction))
        {
            string faction = query.Faction.Trim();
            units = units.Where(unit => unit.Factions.Any(value =>
                string.Equals(value, faction, StringComparison.OrdinalIgnoreCase)));
        }

        if (query.MinRarity is int minRarity)
        {
            units = units.Where(unit => unit.Rarity >= minRarity);
        }

        if (query.MinRelic is int minRelic)
        {
            units = units.Where(unit => !unit.IsShip && unit.RelicTier >= minRelic);
        }

        if (query.HasZeta is bool hasZeta)
        {
            units = units.Where(unit => (unit.ZetaCount > 0) == hasZeta);
        }

        if (query.HasOmicron is bool hasOmicron)
        {
            units = units.Where(unit => (unit.OmicronCount > 0) == hasOmicron);
        }

        PlayerRosterUnit[] filtered = [.. units];
        int total = filtered.Length;
        IOrderedEnumerable<PlayerRosterUnit> ordered = Order(filtered, query.OrderBy, query.Direction)
            .ThenBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(unit => unit.DefinitionId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(unit => unit.Id, StringComparer.OrdinalIgnoreCase);

        long offset = (long)(query.Page - 1) * query.PageSize;
        PlayerRosterUnit[] items = offset >= total
            ? []
            : [.. ordered.Skip((int)offset).Take(query.PageSize)];
        int totalPages = total == 0 ? 0 : (total + query.PageSize - 1) / query.PageSize;

        return new PlayerRosterPage(
            snapshot.AllyCode,
            snapshot.UpdatedAtUtc,
            total,
            query.Page,
            query.PageSize,
            totalPages,
            items,
            snapshot.PlayerName,
            snapshot.GalacticPower,
            snapshot.RosterCount,
            snapshot.AvailableFactions);
    }

    public async Task<PlayerRosterSnapshot?> GetSnapshotAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        PlayerProfile? player = await repository.FindByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        if (cache.TryGet(player.AllyCode, player.UpdatedAtUtc, out PlayerRosterSnapshot? cached))
        {
            return cached;
        }

        IReadOnlyDictionary<string, GameUnitDefinition> gameUnits = rosterGameDataCatalog is null
            ? (await gameDataCatalog.GetAsync(cancellationToken).ConfigureAwait(false)).Units
            : await rosterGameDataCatalog.GetUnitsAsync(cancellationToken).ConfigureAwait(false);

        PlayerRosterUnit[] allUnits = [.. player.Roster.Select(unit => Enrich(unit, gameUnits))];
        string[] availableFactions =
        [
            .. allUnits
                .SelectMany(unit => unit.Factions)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        ];

        var snapshot = new PlayerRosterSnapshot(
            player.AllyCode,
            player.UpdatedAtUtc,
            player.Name,
            player.GalacticPower,
            player.Roster.Count,
            allUnits,
            availableFactions);
        cache.Set(snapshot);
        return snapshot;
    }

    private static PlayerRosterUnit Enrich(
        RosterUnit unit,
        IReadOnlyDictionary<string, GameUnitDefinition> gameUnits)
    {
        gameUnits.TryGetValue(unit.DefinitionId, out GameUnitDefinition? gameUnit);

        return new PlayerRosterUnit(
            unit.Id,
            unit.DefinitionId,
            gameUnit?.Name ?? unit.DefinitionId,
            gameUnit?.NameKey,
            gameUnit?.ThumbnailName,
            gameUnit?.Factions ?? [],
            gameUnit?.Tags ?? [],
            unit.Level,
            unit.Rarity,
            unit.GearTier,
            unit.RelicTier,
            unit.EquippedModCount,
            unit.GalacticPower,
            unit.IsShip,
            unit.ZetaCount,
            unit.OmicronCount,
            unit.Stats,
            unit.Mods);
    }

    private static bool MatchesSearch(PlayerRosterUnit unit, string search) =>
        unit.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || unit.DefinitionId.Contains(search, StringComparison.OrdinalIgnoreCase)
        || unit.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
        || unit.NameKey?.Contains(search, StringComparison.OrdinalIgnoreCase) is true
        || unit.Factions.Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase))
        || unit.Tags.Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase));

    private static IOrderedEnumerable<PlayerRosterUnit> Order(
        IEnumerable<PlayerRosterUnit> units,
        PlayerRosterSortField field,
        PlayerRosterSortDirection direction)
    {
        bool ascending = direction == PlayerRosterSortDirection.Ascending;

        return field switch
        {
            PlayerRosterSortField.RelicTier => ascending
                ? units.OrderBy(unit => unit.RelicTier)
                : units.OrderByDescending(unit => unit.RelicTier),
            PlayerRosterSortField.Rarity => ascending
                ? units.OrderBy(unit => unit.Rarity)
                : units.OrderByDescending(unit => unit.Rarity),
            PlayerRosterSortField.GearTier => ascending
                ? units.OrderBy(unit => unit.GearTier)
                : units.OrderByDescending(unit => unit.GearTier),
            PlayerRosterSortField.Level => ascending
                ? units.OrderBy(unit => unit.Level)
                : units.OrderByDescending(unit => unit.Level),
            PlayerRosterSortField.DefinitionId => ascending
                ? units.OrderBy(unit => unit.DefinitionId, StringComparer.OrdinalIgnoreCase)
                : units.OrderByDescending(unit => unit.DefinitionId, StringComparer.OrdinalIgnoreCase),
            PlayerRosterSortField.Name => ascending
                ? units.OrderBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)
                : units.OrderByDescending(unit => unit.Name, StringComparer.OrdinalIgnoreCase),
            _ => ascending
                ? units.OrderBy(unit => unit.GalacticPower)
                : units.OrderByDescending(unit => unit.GalacticPower)
        };
    }

    private static void Validate(PlayerRosterQuery query)
    {
        if (query.Page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Page, "Page must be at least 1.");
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.PageSize, $"Page size must be between 1 and {MaxPageSize}.");
        }

        if (query.MinRarity is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.MinRarity, "Minimum rarity must be between 1 and 7.");
        }

        if (query.MinRelic is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.MinRelic, "Minimum relic tier must be between 0 and 10.");
        }

        if (!Enum.IsDefined(typeof(PlayerRosterUnitType), query.Type)
            || !Enum.IsDefined(typeof(PlayerRosterSortField), query.OrderBy)
            || !Enum.IsDefined(typeof(PlayerRosterSortDirection), query.Direction))
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Roster query contains an unsupported enum value.");
        }
    }
}
