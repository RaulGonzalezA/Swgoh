using Swgoh.Application.Players;

namespace Swgoh.Application.Gac;

public interface IGacPlannerContextService
{
    Task<GacPlannerContextLookup> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default);
}

public sealed record GacPlannerContext(
    GacPlannerState Planner,
    PlayerRosterSnapshot? PlayerRoster,
    PlayerRosterSnapshot? OpponentRoster);

public sealed record GacPlannerContextLookup(
    CurrentGacOpponentStatus Status,
    string? Message,
    GacPlannerContext? Context)
{
    public bool IsAvailable => Status == CurrentGacOpponentStatus.Found && Context is not null;
}

internal sealed class GacPlannerContextService(
    IGacPlannerService plannerService,
    IPlayerRosterService rosterService) : IGacPlannerContextService
{
    public async Task<GacPlannerContextLookup> GetCurrentAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        GacPlannerLookup lookup = await plannerService
            .GetCurrentAsync(allyCode, cancellationToken)
            .ConfigureAwait(false);
        if (!lookup.IsAvailable || lookup.State is null)
        {
            return new GacPlannerContextLookup(lookup.Status, lookup.Message, null);
        }

        Task<PlayerRosterSnapshot?> playerRosterTask = rosterService.GetSnapshotAsync(
            allyCode,
            cancellationToken);
        Task<PlayerRosterSnapshot?> opponentRosterTask = rosterService.GetSnapshotAsync(
            lookup.State.Opponent.OpponentAllyCode,
            cancellationToken);

        await Task.WhenAll(playerRosterTask, opponentRosterTask).ConfigureAwait(false);

        return new GacPlannerContextLookup(
            CurrentGacOpponentStatus.Found,
            null,
            new GacPlannerContext(
                lookup.State,
                await playerRosterTask.ConfigureAwait(false),
                await opponentRosterTask.ConfigureAwait(false)));
    }
}
