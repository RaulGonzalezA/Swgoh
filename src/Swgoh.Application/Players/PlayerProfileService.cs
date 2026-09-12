using Swgoh.Application.Abstractions;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

internal sealed class PlayerProfileService(
    IPlayerRepository repository,
    IPlayerSnapshotRepository snapshotRepository,
    ISwgohPlayerClient swgohPlayerClient,
    IClock clock) : IPlayerProfileService
{
    public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
        repository.FindByAllyCodeAsync(allyCode, cancellationToken);

    public async Task<PlayerProfile> SaveAsync(long allyCode, string name, long galacticPower, CancellationToken cancellationToken = default)
    {
        PlayerProfile? player = await repository.FindByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);

        if (player is null)
        {
            player = PlayerProfile.Create(allyCode, name, galacticPower, clock.UtcNow);
        }
        else
        {
            player.Refresh(name, galacticPower, clock.UtcNow);
        }

        await repository.UpsertAsync(player, cancellationToken).ConfigureAwait(false);
        return player;
    }

    public async Task<PlayerProfile> RefreshFromGameAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        ImportedPlayer imported = await swgohPlayerClient.GetPlayerAsync(allyCode, cancellationToken).ConfigureAwait(false);
        RosterUnit[] roster =
        [
            .. imported.Roster.Select(unit => new RosterUnit(
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
                unit.OmicronCount))
        ];

        PlayerProfile player = PlayerProfile.Import(
            imported.AllyCode,
            imported.PlayerId,
            imported.Name,
            imported.GuildId,
            imported.GuildName,
            imported.Level,
            imported.GalacticPower,
            roster,
            clock.UtcNow);

        await repository.UpsertAsync(player, cancellationToken).ConfigureAwait(false);
        await snapshotRepository.UpsertAsync(PlayerRosterMetrics.CreateSnapshot(player), cancellationToken).ConfigureAwait(false);
        return player;
    }
}
