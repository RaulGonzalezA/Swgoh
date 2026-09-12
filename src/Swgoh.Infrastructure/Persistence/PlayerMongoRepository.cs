using RepositoryMongoDb.Repository;

using Swgoh.Application.Players;
using Swgoh.Domain.Players;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class PlayerMongoRepository(IMongoDbRepository<PlayerDocument, long> repository) : IPlayerRepository
{
    internal const string CollectionName = "players";

    public async Task<PlayerProfile?> FindByAllyCodeAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        PlayerDocument? document = await repository.FindByIdAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public Task UpsertAsync(PlayerProfile player, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        return repository.UpsertAsync(ToDocument(player), cancellationToken);
    }

    private static PlayerDocument ToDocument(PlayerProfile player) => new()
    {
        AllyCode = player.AllyCode,
        PlayerId = player.PlayerId,
        Name = player.Name,
        GuildId = player.GuildId,
        GuildName = player.GuildName,
        Level = player.Level,
        GalacticPower = player.GalacticPower,
        UpdatedAtUtc = player.UpdatedAtUtc,
        Roster =
        [
            .. player.Roster.Select(unit => new RosterUnitDocument
            {
                Id = unit.Id,
                DefinitionId = unit.DefinitionId,
                Level = unit.Level,
                Rarity = unit.Rarity,
                GearTier = unit.GearTier,
                RelicTier = unit.RelicTier,
                EquippedModCount = unit.EquippedModCount,
                GalacticPower = unit.GalacticPower,
                IsShip = unit.IsShip,
                ZetaCount = unit.ZetaCount,
                OmicronCount = unit.OmicronCount
            })
        ]
    };

    private static PlayerProfile ToDomain(PlayerDocument document) => PlayerProfile.Import(
        document.AllyCode,
        document.PlayerId,
        document.Name,
        document.GuildId,
        document.GuildName,
        document.Level,
        document.GalacticPower,
        document.UpdatedAtUtc,
        document.Roster.Select(unit => new RosterUnit(
            unit.Id,
            unit.DefinitionId,
            unit.Level,
            unit.Rarity,
            unit.GearTier,
            unit.RelicTier,
            unit.EquippedModCount,
            unit.GalacticPower,
            unit.IsShip,
            unit.ZetaCount,
            unit.OmicronCount)));
}
