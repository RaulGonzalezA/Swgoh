using Swgoh.Application.Abstractions;
using Swgoh.Application.GameData;
using Swgoh.Domain.Squads;

namespace Swgoh.Application.Squads;

internal sealed class SquadService(
    ISquadRepository repository,
    ISwgohGameDataCatalog gameDataCatalog,
    IClock clock) : ISquadService
{
    private const int MaxSearchLimit = 200;

    public async Task<SquadDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        SquadDefinition? squad = await repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (squad is null)
        {
            return null;
        }

        GameDataCatalog catalog = await gameDataCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
        return ToDetails(squad, catalog);
    }

    public async Task<IReadOnlyCollection<SquadDetails>> SearchAsync(
        SquadSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Limit, MaxSearchLimit, nameof(query));

        SquadSearchQuery normalized = query with
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Tag = string.IsNullOrWhiteSpace(query.Tag) ? null : query.Tag.Trim().ToLowerInvariant()
        };

        IReadOnlyCollection<SquadDefinition> squads = await repository
            .SearchAsync(normalized, cancellationToken)
            .ConfigureAwait(false);
        GameDataCatalog catalog = await gameDataCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
        return [.. squads.Select(squad => ToDetails(squad, catalog))];
    }

    public async Task<SquadDetails> CreateAsync(
        SaveSquadDefinition input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        GameDataCatalog catalog = await gameDataCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
        SquadVariant[] variants = CreateVariants(input, catalog);
        DateTimeOffset now = clock.UtcNow;
        SquadDefinition squad = SquadDefinition.Create(
            Guid.NewGuid(),
            input.Name,
            input.Format,
            input.Use,
            input.Tags,
            variants,
            now);

        await repository.UpsertAsync(squad, cancellationToken).ConfigureAwait(false);
        return ToDetails(squad, catalog);
    }

    public async Task<SquadDetails?> UpdateAsync(
        Guid id,
        SaveSquadDefinition input,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        ArgumentNullException.ThrowIfNull(input);

        SquadDefinition? squad = await repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (squad is null)
        {
            return null;
        }

        GameDataCatalog catalog = await gameDataCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
        SquadVariant[] variants = CreateVariants(input, catalog);
        squad.Update(input.Name, input.Format, input.Use, input.Tags, variants, clock.UtcNow);
        await repository.UpsertAsync(squad, cancellationToken).ConfigureAwait(false);
        return ToDetails(squad, catalog);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        return repository.DeleteAsync(id, cancellationToken);
    }

    private static SquadVariant[] CreateVariants(SaveSquadDefinition input, GameDataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(input.Tags);
        ArgumentNullException.ThrowIfNull(input.Variants);

        return
        [
            .. input.Variants.Select(variant =>
            {
                ArgumentNullException.ThrowIfNull(variant);
                string leader = ResolveCharacterDefinitionId(variant.LeaderDefinitionId, catalog);
                string[] members = [.. variant.MemberDefinitionIds.Select(id => ResolveCharacterDefinitionId(id, catalog))];
                return SquadVariant.Create(input.Format, variant.Key, variant.Name, leader, members);
            })
        ];
    }

    private static string ResolveCharacterDefinitionId(string definitionId, GameDataCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        string requested = definitionId.Trim();
        if (!catalog.Units.TryGetValue(requested, out GameUnitDefinition? unit))
        {
            unit = catalog.Units.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.BaseId, requested, StringComparison.OrdinalIgnoreCase));
        }

        if (unit is null)
        {
            throw new ArgumentException($"Unknown SWGOH unit definition ID '{requested}'.", nameof(definitionId));
        }

        if (unit.IsShip)
        {
            throw new ArgumentException($"Ship '{unit.BaseId}' cannot be used in a character squad.", nameof(definitionId));
        }

        return unit.BaseId;
    }

    private static SquadDetails ToDetails(SquadDefinition squad, GameDataCatalog catalog) => new(
        squad.Id,
        squad.Name,
        squad.Format,
        squad.Use,
        squad.Tags,
        [.. squad.Variants.Select(variant => new SquadVariantDetails(
            variant.Key,
            variant.Name,
            ToUnitDetails(variant.LeaderDefinitionId, catalog),
            [.. variant.MemberDefinitionIds.Select(id => ToUnitDetails(id, catalog))]))],
        squad.CreatedAtUtc,
        squad.UpdatedAtUtc);

    private static SquadUnitDetails ToUnitDetails(string definitionId, GameDataCatalog catalog)
    {
        if (!catalog.Units.TryGetValue(definitionId, out GameUnitDefinition? unit))
        {
            return new SquadUnitDetails(definitionId, definitionId, null, []);
        }

        return new SquadUnitDetails(unit.BaseId, unit.Name, unit.ThumbnailName, unit.Factions);
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Squad ID cannot be empty.", nameof(id));
        }
    }
}
