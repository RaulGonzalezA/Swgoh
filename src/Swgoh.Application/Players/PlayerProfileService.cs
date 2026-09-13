using Swgoh.Application.Abstractions;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

internal sealed class PlayerProfileService(
    IPlayerRepository repository,
    IPlayerSnapshotRepository snapshotRepository,
    ISwgohPlayerClient swgohPlayerClient,
    IClock clock,
    PlayerRefreshLock refreshLock) : IPlayerProfileService
{
    internal static readonly TimeSpan RefreshFreshnessWindow = TimeSpan.FromMinutes(5);

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
        PlayerProfile? existing = await repository.FindByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (IsFreshImportedProfile(existing))
        {
            await EnsureSnapshotExistsAsync(existing!, cancellationToken).ConfigureAwait(false);
            return existing!;
        }

        using IDisposable refreshLease = await refreshLock.AcquireAsync(allyCode, cancellationToken).ConfigureAwait(false);

        existing = await repository.FindByAllyCodeAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (IsFreshImportedProfile(existing))
        {
            await EnsureSnapshotExistsAsync(existing!, cancellationToken).ConfigureAwait(false);
            return existing!;
        }

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
            clock.UtcNow,
            roster);

        await repository.UpsertAsync(player, cancellationToken).ConfigureAwait(false);
        await snapshotRepository.UpsertAsync(PlayerRosterMetrics.CreateSnapshot(player), cancellationToken).ConfigureAwait(false);
        return player;
    }

    private async Task EnsureSnapshotExistsAsync(PlayerProfile player, CancellationToken cancellationToken)
    {
        PlayerSnapshot snapshot = PlayerRosterMetrics.CreateSnapshot(player);
        if (!await snapshotRepository.ExistsAsync(snapshot.Id, cancellationToken).ConfigureAwait(false))
        {
            await snapshotRepository.UpsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool IsFreshImportedProfile(PlayerProfile? player) =>
        player is not null
        && !string.IsNullOrWhiteSpace(player.PlayerId)
        && player.UpdatedAtUtc >= clock.UtcNow - RefreshFreshnessWindow;
}
