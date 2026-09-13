namespace Swgoh.Application.Gac;

public interface IGacAttackPlanOptimizerService
{
    Task<GacAttackOptimizationLookup> OptimizeCurrentAsync(
        long allyCode,
        GacAttackOptimizationMode mode,
        bool apply,
        CancellationToken cancellationToken = default);
}
