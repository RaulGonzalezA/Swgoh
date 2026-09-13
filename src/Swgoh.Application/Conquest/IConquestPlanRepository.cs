using Swgoh.Domain.Conquest;

namespace Swgoh.Application.Conquest;

public interface IConquestPlanRepository
{
    Task<ConquestPlan?> FindByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<ConquestPlan?> GetCurrentAsync(long allyCode, CancellationToken cancellationToken = default);

    Task UpsertAsync(ConquestPlan plan, CancellationToken cancellationToken = default);
}
