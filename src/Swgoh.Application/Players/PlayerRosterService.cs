using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

internal sealed class PlayerRosterService(IPlayerRepository repository) : IPlayerRosterService
{
    private const int MaxPageSize = 100;

    public async Task<PlayerRosterPage?> GetAsync(
        long allyCode,
        PlayerRosterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);

        PlayerProfile? player = await repository.FindByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        IEnumerable<RosterUnit> units = player.Roster;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.Trim();
            units = units.Where(unit =>
                unit.DefinitionId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || unit.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        units = query.Type switch
        {
            PlayerRosterUnitType.Character => units.Where(unit => !unit.IsShip),
            PlayerRosterUnitType.Ship => units.Where(unit => unit.IsShip),
            _ => units
        };

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

        RosterUnit[] filtered = [.. units];
        int total = filtered.Length;
        IOrderedEnumerable<RosterUnit> ordered = Order(filtered, query.OrderBy, query.Direction)
            .ThenBy(unit => unit.DefinitionId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(unit => unit.Id, StringComparer.OrdinalIgnoreCase);

        long offset = (long)(query.Page - 1) * query.PageSize;
        RosterUnit[] items = offset >= total
            ? []
            : [.. ordered.Skip((int)offset).Take(query.PageSize)];
        int totalPages = total == 0 ? 0 : (total + query.PageSize - 1) / query.PageSize;

        return new PlayerRosterPage(
            player.AllyCode,
            player.UpdatedAtUtc,
            total,
            query.Page,
            query.PageSize,
            totalPages,
            items);
    }

    private static IOrderedEnumerable<RosterUnit> Order(
        IEnumerable<RosterUnit> units,
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
