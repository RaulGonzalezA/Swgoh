using Swgoh.Application.Abstractions;
using Swgoh.Domain.Players;

namespace Swgoh.Application.Players;

internal sealed class PlayerProfileService(
    IPlayerRepository repository,
    ISwgohPlayerClient swgohPlayerClient,
    IClock clock) : IPlayerProfileService
{
    public Task<PlayerProfile?> GetAsync(long allyCode, CancellationToken cancellationToken = default) =>
        repository.FindByAllyCodeAsync(allyCode, cancellationToken);

    public async Task<PlayerProfile> SaveAsync(
        long allyCode,
        string name,
        long galacticPower,
        CancellationToken cancellationToken = default)
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

    public async Task<PlayerProfile> RefreshFromGameAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        ImportedPlayer source = await swgohPlayerClient.GetPlayerAsync(allyCode, cancellationToken).ConfigureAwait(false);

        RosterUnit[] roster = [.. source.Roster.Select(unit => new RosterUnit(
            unit.Id,
            unit.DefinitionId,
            unit.Level,
            unit.Rarity,
            unit.GearTier,
            unit.RelicTier,
            unit.EquippedModCount))];

        PlayerProfile player = PlayerProfile.Import(
            source.AllyCode,
            source.PlayerId,
            source.Name,
            source.GuildId,
            source.GuildName,
            source.Level,
            source.GalacticPower,
            clock.UtcNow,
            roster);

        await repository.UpsertAsync(player, cancellationToken).ConfigureAwait(false);
        return player;
    }
}
