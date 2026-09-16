namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireGuildRoutePlanner
{
    public static IReadOnlyCollection<RiseOfEmpireGuildPhasePlan> Build(
        long guildGalacticPower,
        IReadOnlyCollection<RiseOfEmpireOperationPlan> operations,
        IReadOnlyCollection<RiseOfEmpireBonusUnlockReadiness> bonusUnlocks)
    {
        var phases = new List<RiseOfEmpireGuildPhasePlan>();
        for (int phase = 1; phase <= 6; phase++)
        {
            RiseOfEmpirePlanetDefinition[] planets =
            [
                .. RiseOfEmpireCatalog.Planets
                    .Where(planet => planet.Phase == phase)
                    .OrderBy(planet => planet.Name, StringComparer.OrdinalIgnoreCase)
            ];
            RiseOfEmpireOperationPlan[] phaseOperations = [.. operations.Where(operation => operation.Phase == phase)];
            long forcedGp = phaseOperations.Sum(operation => operation.ForcedDeploymentGalacticPower);
            long remainingGp = Math.Max(0, guildGalacticPower - forcedGp);
            PlanetOption[] selected = SelectBestOptions(planets, phaseOperations, bonusUnlocks, remainingGp);
            phases.Add(new RiseOfEmpireGuildPhasePlan(
                phase,
                guildGalacticPower,
                forcedGp,
                selected.Sum(option => option.Stars),
                [.. selected.Select(option => ToPlan(option, phaseOperations))]));
        }

        return phases;
    }

    private static PlanetOption[] SelectBestOptions(
        IReadOnlyCollection<RiseOfEmpirePlanetDefinition> planets,
        IReadOnlyCollection<RiseOfEmpireOperationPlan> operations,
        IReadOnlyCollection<RiseOfEmpireBonusUnlockReadiness> bonusUnlocks,
        long remainingGp)
    {
        PlanetOption[][] options =
        [
            .. planets.Select(planet => BuildOptions(planet, operations, bonusUnlocks))
        ];
        PlanetOption[] best = [];
        int bestStars = -1;
        long bestDeployment = long.MaxValue;
        var current = new PlanetOption[options.Length];

        Search(0, 0, 0);
        return best;

        void Search(int index, int stars, long deployment)
        {
            if (deployment > remainingGp)
            {
                return;
            }

            if (index == options.Length)
            {
                if (stars > bestStars || (stars == bestStars && deployment < bestDeployment))
                {
                    bestStars = stars;
                    bestDeployment = deployment;
                    best = [.. current];
                }

                return;
            }

            foreach (PlanetOption option in options[index])
            {
                current[index] = option;
                Search(index + 1, stars + option.Stars, deployment + option.AdditionalDeployment);
            }
        }
    }

    private static PlanetOption[] BuildOptions(
        RiseOfEmpirePlanetDefinition planet,
        IReadOnlyCollection<RiseOfEmpireOperationPlan> operations,
        IReadOnlyCollection<RiseOfEmpireBonusUnlockReadiness> bonusUnlocks)
    {
        bool available = !planet.IsBonusZone || bonusUnlocks.Any(unlock =>
            unlock.ProjectedUnlocked && string.Equals(unlock.PlanetName, planet.Name, StringComparison.OrdinalIgnoreCase));
        long operationPoints = operations
            .Where(operation => string.Equals(operation.PlanetName, planet.Name, StringComparison.OrdinalIgnoreCase))
            .Sum(operation => operation.CompletedPoints);
        long forcedGp = operations
            .Where(operation => string.Equals(operation.PlanetName, planet.Name, StringComparison.OrdinalIgnoreCase))
            .Sum(operation => operation.ForcedDeploymentGalacticPower);
        if (!available)
        {
            return [new PlanetOption(planet, false, 0, 0, operationPoints, forcedGp)];
        }

        var result = new List<PlanetOption>
        {
            new(planet, true, 0, 0, operationPoints, forcedGp)
        };
        for (int stars = 1; stars <= Math.Min(3, planet.StarThresholds.Count); stars++)
        {
            long threshold = planet.StarThresholds.ElementAt(stars - 1);
            long additional = Math.Max(0, threshold - operationPoints - forcedGp);
            result.Add(new PlanetOption(planet, true, stars, additional, operationPoints, forcedGp));
        }

        return [.. result];
    }

    private static RiseOfEmpireGuildPlanetPlan ToPlan(
        PlanetOption option,
        IReadOnlyCollection<RiseOfEmpireOperationPlan> operations)
    {
        long threshold = option.Stars == 0
            ? 0
            : option.Planet.StarThresholds.ElementAt(option.Stars - 1);
        string reason = !option.Available
            ? "Zona bonus todavía no desbloqueada por suficientes miembros preparados."
            : option.Stars == 0
                ? "El GP restante se aprovecha mejor en otros planetas de esta fase."
                : option.OperationPoints > 0
                    ? "Objetivo calculado con operaciones completas y despliegue adicional, sin contar puntos de combate."
                    : "Objetivo conservador basado en despliegue; los puntos de combate pueden reducir el GP necesario.";
        return new RiseOfEmpireGuildPlanetPlan(
            option.Planet.Id,
            option.Planet.Name,
            option.Planet.IsBonusZone,
            option.Available,
            option.Stars,
            threshold,
            option.OperationPoints,
            option.ForcedGp,
            option.AdditionalDeployment,
            reason);
    }

    private sealed record PlanetOption(
        RiseOfEmpirePlanetDefinition Planet,
        bool Available,
        int Stars,
        long AdditionalDeployment,
        long OperationPoints,
        long ForcedGp);
}
