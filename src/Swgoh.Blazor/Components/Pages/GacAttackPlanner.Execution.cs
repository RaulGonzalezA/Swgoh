using Swgoh.Blazor.Clients;

namespace Swgoh.Blazor.Components.Pages;

public partial class GacAttackPlanner
{
    protected IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> ExecutionOwnRoster => playerRoster;

    protected IReadOnlyCollection<PlayerApiClient.RosterUnitViewModel> ExecutionOpponentRoster => opponentRoster;

    protected Task HandleAttackExecutedAsync(GacAttackExecutionApiClient.ExecutionEnvelopeViewModel envelope)
    {
        Planner = envelope.Planner;
        Optimization = envelope.Optimization;
        MapDraftsFromPlanner();
        return Task.CompletedTask;
    }
}
