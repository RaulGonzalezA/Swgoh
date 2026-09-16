using System.Collections.Concurrent;

using Swgoh.Application.Abstractions;
using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

namespace Swgoh.Application.TerritoryBattles;

internal sealed class RiseOfEmpireGuildService(
    IPlayerProfileService playerProfileService,
    IRiseOfEmpireGuildPlayerRepository playerRepository,
    IRiseOfEmpireGuildSource guildSource,
    IRiseOfEmpireOperationsCatalog operationsCatalog,
    ISwgohGameDataCatalog gameDataCatalog,
    IRiseOfEmpireService individualService,
    IClock clock) : IRiseOfEmpireGuildService
{
    private const int MaxRefreshParallelism = 4;
    private const int MaxAnalysisParallelism = 8;

    public async Task<RiseOfEmpireGuildAnalysis?> GetAsync(
        long allyCode,
        bool refreshGuildRoster,
        CancellationToken cancellationToken = default)
    {
        PlayerProfile? seed = await playerProfileService.GetAsync(allyCode, cancellationToken).ConfigureAwait(false);
        if (seed is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(seed.GuildId))
        {
            throw new InvalidOperationException("El jugador no tiene un gremio disponible en el perfil importado.");
        }

        var warnings = new List<string>();
        RiseOfEmpireGuildSnapshot? snapshot = null;
        if (refreshGuildRoster)
        {
            snapshot = await guildSource.GetAsync(seed.GuildId, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(snapshot.Warnings);
            warnings.AddRange(await RefreshMembersAsync(snapshot.Members, cancellationToken).ConfigureAwait(false));
        }
        else
        {
            warnings.Add("El plan usa los rosters de gremio almacenados. Sincroniza el gremio para actualizar miembros y reliquias.");
        }

        PlayerProfile[] cached =
        [
            .. await playerRepository.FindByGuildIdAsync(seed.GuildId, 50, cancellationToken).ConfigureAwait(false)
        ];
        PlayerProfile[] players = FilterCurrentMembers(cached, snapshot);
        if (!players.Any(player => player.AllyCode == seed.AllyCode))
        {
            players = [.. players, seed];
        }

        int detectedMembers = snapshot?.DetectedMembers ?? players.Length;
        string guildName = snapshot?.GuildName ?? seed.GuildName ?? "Gremio";
        long guildGp = snapshot?.GalacticPower > 0
            ? snapshot.GalacticPower
            : players.Sum(player => player.GalacticPower);
        if (detectedMembers > players.Length)
        {
            warnings.Add($"Se detectaron {detectedMembers} miembros, pero hay {players.Length} rosters utilizables; operaciones y mejoras usan solo los importados.");
        }

        Task<IReadOnlyCollection<RiseOfEmpireOperationDefinition>> operationsTask = operationsCatalog.GetAsync(cancellationToken);
        Task<GameDataCatalog> gameDataTask = gameDataCatalog.GetAsync(cancellationToken);
        await Task.WhenAll(operationsTask, gameDataTask).ConfigureAwait(false);
        IReadOnlyCollection<RiseOfEmpireOperationDefinition> operationDefinitions = await operationsTask.ConfigureAwait(false);
        GameDataCatalog gameData = await gameDataTask.ConfigureAwait(false);

        IReadOnlyCollection<RiseOfEmpireBonusUnlockReadiness> bonusUnlocks =
            RiseOfEmpireBonusUnlockAnalyzer.Analyze(players, gameData);
        HashSet<string> unlockedBonusPlanets = bonusUnlocks
            .Where(unlock => unlock.ProjectedUnlocked)
            .Select(unlock => unlock.PlanetName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        RiseOfEmpireOperationDefinition[] availableOperations =
        [
            .. operationDefinitions.Where(operation =>
                !operation.IsBonus || unlockedBonusPlanets.Contains(operation.PlanetName))
        ];

        IReadOnlyCollection<RiseOfEmpireOperationPlan> operationPlans =
            RiseOfEmpireOperationAllocator.Allocate(players, availableOperations, gameData);
        IReadOnlyCollection<RiseOfEmpireGuildPhasePlan> phases =
            RiseOfEmpireGuildRoutePlanner.Build(guildGp, operationPlans, bonusUnlocks);
        IReadOnlyCollection<RiseOfEmpireAnalysis> individualAnalyses =
            await GetIndividualAnalysesAsync(players, cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<RiseOfEmpireGuildUpgradePriority> upgrades =
            RiseOfEmpireGuildUpgradePlanner.Build(players, operationPlans, individualAnalyses, gameData);

        warnings.Add("La ruta de estrellas es conservadora: cuenta despliegue y operaciones completas, pero no presupone victorias ni puntos de misiones de combate.");
        warnings.Add("La preparación de Zeffo y Mandalore cuenta miembros con requisitos de roster; la victoria de la misión de desbloqueo no se da por garantizada.");

        return new RiseOfEmpireGuildAnalysis(
            snapshot?.GuildId ?? seed.GuildId,
            guildName,
            guildGp,
            detectedMembers,
            players.Length,
            clock.UtcNow,
            [.. warnings.Distinct(StringComparer.OrdinalIgnoreCase)],
            phases,
            operationPlans,
            bonusUnlocks,
            upgrades);
    }

    private async Task<IReadOnlyCollection<string>> RefreshMembersAsync(
        IReadOnlyCollection<RiseOfEmpireGuildMemberReference> members,
        CancellationToken cancellationToken)
    {
        var warnings = new ConcurrentBag<string>();
        using var semaphore = new SemaphoreSlim(MaxRefreshParallelism, MaxRefreshParallelism);
        Task[] tasks =
        [
            .. members.Select(async member =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await playerProfileService.RefreshFromGameAsync(member.AllyCode, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    warnings.Add($"No se pudo actualizar el roster de {member.PlayerName}.");
                }
                catch (InvalidOperationException)
                {
                    warnings.Add($"No se pudo persistir o analizar el roster de {member.PlayerName}.");
                }
                finally
                {
                    semaphore.Release();
                }
            })
        ];
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return [.. warnings.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<IReadOnlyCollection<RiseOfEmpireAnalysis>> GetIndividualAnalysesAsync(
        IReadOnlyCollection<PlayerProfile> players,
        CancellationToken cancellationToken)
    {
        var analyses = new ConcurrentBag<RiseOfEmpireAnalysis>();
        using var semaphore = new SemaphoreSlim(MaxAnalysisParallelism, MaxAnalysisParallelism);
        Task[] tasks =
        [
            .. players.Select(async player =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RiseOfEmpireAnalysis? analysis = await individualService.GetAsync(player.AllyCode, cancellationToken)
                        .ConfigureAwait(false);
                    if (analysis is not null)
                    {
                        analyses.Add(analysis);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            })
        ];
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return [.. analyses];
    }

    private static PlayerProfile[] FilterCurrentMembers(
        IReadOnlyCollection<PlayerProfile> cached,
        RiseOfEmpireGuildSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return [.. cached];
        }

        HashSet<long> current = snapshot.Members.Select(member => member.AllyCode).ToHashSet();
        return [.. cached.Where(player => current.Contains(player.AllyCode))];
    }
}
