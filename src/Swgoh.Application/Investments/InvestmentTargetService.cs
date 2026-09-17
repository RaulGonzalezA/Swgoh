using Swgoh.Application.Abstractions;
using Swgoh.Application.Players;

namespace Swgoh.Application.Investments;

public interface IInvestmentTargetService
{
    Task<IReadOnlyCollection<InvestmentTargetProgress>> GetAllAsync(
        long allyCode,
        CancellationToken cancellationToken = default);

    Task<InvestmentTargetProgress?> GetAsync(
        long allyCode,
        string definitionId,
        CancellationToken cancellationToken = default);

    Task<InvestmentTargetProgress> SaveAsync(
        long allyCode,
        string definitionId,
        InvestmentTargetUpdate update,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        long allyCode,
        string definitionId,
        CancellationToken cancellationToken = default);
}

internal sealed class InvestmentTargetService(
    IInvestmentTargetRepository repository,
    IPlayerRosterService rosterService,
    IPlayerInventoryService inventoryService,
    IClock clock) : IInvestmentTargetService
{
    public async Task<IReadOnlyCollection<InvestmentTargetProgress>> GetAllAsync(
        long allyCode,
        CancellationToken cancellationToken = default)
    {
        ValidateAllyCode(allyCode);
        Task<IReadOnlyCollection<InvestmentTarget>> targetsTask = repository.GetAllAsync(allyCode, cancellationToken);
        Task<PlayerRosterSnapshot?> rosterTask = rosterService.GetSnapshotAsync(allyCode, cancellationToken);
        Task<PlayerInventorySnapshot?> inventoryTask = inventoryService.GetAsync(allyCode, cancellationToken);
        await Task.WhenAll(targetsTask, rosterTask, inventoryTask).ConfigureAwait(false);

        IReadOnlyCollection<InvestmentTarget> targets = await targetsTask.ConfigureAwait(false);
        PlayerRosterSnapshot? roster = await rosterTask.ConfigureAwait(false);
        PlayerInventorySnapshot? inventory = await inventoryTask.ConfigureAwait(false);
        if (roster is null || targets.Count == 0)
        {
            return [];
        }

        var units = roster.Units.ToDictionary(unit => unit.DefinitionId, StringComparer.OrdinalIgnoreCase);
        return
        [
            .. targets
                .Select(target => units.TryGetValue(target.DefinitionId, out PlayerRosterUnit? unit)
                    ? BuildProgress(target, unit, inventory)
                    : null)
                .Where(progress => progress is not null)
                .Select(progress => progress!)
                .OrderBy(progress => progress.Completed)
                .ThenByDescending(progress => progress.UpdatedAtUtc)
                .ThenBy(progress => progress.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public async Task<InvestmentTargetProgress?> GetAsync(
        long allyCode,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        ValidateAllyCode(allyCode);
        string normalizedDefinitionId = NormalizeDefinitionId(definitionId);
        InvestmentTarget? target = await repository
            .GetAsync(allyCode, normalizedDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        if (target is null)
        {
            return null;
        }

        PlayerRosterSnapshot? roster = await rosterService.GetSnapshotAsync(allyCode, cancellationToken).ConfigureAwait(false);
        PlayerRosterUnit? unit = roster?.Units.FirstOrDefault(candidate =>
            string.Equals(candidate.DefinitionId, normalizedDefinitionId, StringComparison.OrdinalIgnoreCase));
        if (unit is null)
        {
            return null;
        }

        PlayerInventorySnapshot? inventory = await inventoryService.GetAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return BuildProgress(target, unit, inventory);
    }

    public async Task<InvestmentTargetProgress> SaveAsync(
        long allyCode,
        string definitionId,
        InvestmentTargetUpdate update,
        CancellationToken cancellationToken = default)
    {
        ValidateAllyCode(allyCode);
        ArgumentNullException.ThrowIfNull(update);
        string normalizedDefinitionId = NormalizeDefinitionId(definitionId);
        PlayerRosterSnapshot? roster = await rosterService.GetSnapshotAsync(allyCode, cancellationToken).ConfigureAwait(false);
        PlayerRosterUnit? unit = roster?.Units.FirstOrDefault(candidate =>
            string.Equals(candidate.DefinitionId, normalizedDefinitionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Id, normalizedDefinitionId, StringComparison.OrdinalIgnoreCase));
        if (unit is null)
        {
            throw new ArgumentException("The unit does not exist in the current player roster.", nameof(definitionId));
        }

        ValidateTarget(unit, update);
        InvestmentTarget? existing = await repository
            .GetAsync(allyCode, unit.DefinitionId, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset now = clock.UtcNow;
        var target = new InvestmentTarget(
            allyCode,
            unit.DefinitionId,
            update.TargetRelicTier,
            update.TargetStars,
            existing?.CreatedAtUtc ?? now,
            now);
        await repository.UpsertAsync(target, cancellationToken).ConfigureAwait(false);
        PlayerInventorySnapshot? inventory = await inventoryService.GetAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return BuildProgress(target, unit, inventory);
    }

    public Task<bool> DeleteAsync(
        long allyCode,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        ValidateAllyCode(allyCode);
        return repository.DeleteAsync(allyCode, NormalizeDefinitionId(definitionId), cancellationToken);
    }

    private static InvestmentTargetProgress BuildProgress(
        InvestmentTarget target,
        PlayerRosterUnit unit,
        PlayerInventorySnapshot? inventory)
    {
        bool relicComplete = target.TargetRelicTier is not int targetRelic || unit.RelicTier >= targetRelic;
        bool starsComplete = target.TargetStars is not int targetStars || unit.Rarity >= targetStars;
        bool completed = relicComplete && starsComplete;
        int relicSteps = target.TargetRelicTier is int relic ? Math.Max(0, relic - unit.RelicTier) : 0;
        int starSteps = target.TargetStars is int stars ? Math.Max(0, stars - unit.Rarity) : 0;
        decimal progress = CalculateProgress(unit, target);
        InvestmentInventoryFit? inventoryFit = completed
            ? null
            : BuildInventoryFit(inventory, unit.RelicTier, target.TargetRelicTier);

        return new InvestmentTargetProgress(
            target.AllyCode,
            unit.DefinitionId,
            unit.Name,
            unit.ThumbnailName,
            unit.IsShip,
            unit.RelicTier,
            unit.Rarity,
            target.TargetRelicTier,
            target.TargetStars,
            completed,
            progress,
            relicSteps,
            starSteps,
            SuggestedAction(unit, target, completed),
            inventoryFit,
            target.CreatedAtUtc,
            target.UpdatedAtUtc);
    }

    private static decimal CalculateProgress(PlayerRosterUnit unit, InvestmentTarget target)
    {
        var values = new List<decimal>(2);
        if (target.TargetRelicTier is int relic && relic > 0)
        {
            values.Add(Math.Min(1m, (decimal)unit.RelicTier / relic));
        }

        if (target.TargetStars is int stars && stars > 0)
        {
            values.Add(Math.Min(1m, (decimal)unit.Rarity / stars));
        }

        return values.Count == 0 ? 1m : Math.Round(values.Average(), 3);
    }

    private static string SuggestedAction(PlayerRosterUnit unit, InvestmentTarget target, bool completed)
    {
        if (completed)
        {
            return "Objetivo completado";
        }

        var values = new List<string>(2);
        if (target.TargetRelicTier is int relic && relic > unit.RelicTier)
        {
            values.Add($"R{unit.RelicTier} → R{relic}");
        }

        if (target.TargetStars is int stars && stars > unit.Rarity)
        {
            values.Add($"{unit.Rarity}★ → {stars}★");
        }

        return string.Join(" · ", values);
    }

    private static InvestmentInventoryFit? BuildInventoryFit(
        PlayerInventorySnapshot? inventory,
        int currentRelic,
        int? targetRelic)
    {
        if (inventory is null || targetRelic is not int relic || relic <= currentRelic)
        {
            return null;
        }

        IReadOnlyDictionary<string, long> requirements = RelicMaterialRequirements.Calculate(currentRelic, relic);
        if (requirements.Count == 0)
        {
            return null;
        }

        IReadOnlyDictionary<string, long> available = inventory.Resources
            .GroupBy(resource => resource.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(resource => resource.Quantity), StringComparer.Ordinal);
        InvestmentResourceNeed[] needs =
        [
            .. requirements.Select(pair =>
            {
                InventoryResourceDefinition definition = PlayerInventoryCatalog.Resources
                    .First(resource => string.Equals(resource.Id, pair.Key, StringComparison.Ordinal));
                long owned = available.GetValueOrDefault(pair.Key);
                long missing = Math.Max(0, pair.Value - owned);
                return new InvestmentResourceNeed(pair.Key, definition.Name, pair.Value, owned, missing, missing == 0);
            })
        ];
        decimal coverage = Math.Round(needs.Average(need => need.Required == 0
            ? 1m
            : Math.Min(1m, (decimal)need.Available / need.Required)), 3);
        int missingTypes = needs.Count(need => !need.Sufficient);
        bool materialsReady = missingTypes == 0;
        string summary = materialsReady
            ? "El snapshot cubre todos los materiales de reliquia contabilizados para este objetivo."
            : $"Faltan materiales en {missingTypes} tipo{(missingTypes == 1 ? string.Empty : "s")} de recurso; cobertura {coverage:P0}.";
        return new InvestmentInventoryFit(
            inventory.CapturedAtUtc,
            inventory.Source,
            materialsReady,
            coverage,
            missingTypes,
            summary,
            [.. needs.OrderBy(need => PlayerInventoryCatalog.Resources.First(resource => resource.Id == need.ResourceId).SortOrder)]);
    }

    private static void ValidateTarget(PlayerRosterUnit unit, InvestmentTargetUpdate update)
    {
        if (update.TargetRelicTier is null && update.TargetStars is null)
        {
            throw new ArgumentException("At least one target value is required.", nameof(update));
        }

        if (update.TargetRelicTier is int relic)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(relic, 1, nameof(update));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(relic, 10, nameof(update));
            if (unit.IsShip)
            {
                throw new ArgumentException("Ships cannot have a relic target.", nameof(update));
            }
        }

        if (update.TargetStars is int stars)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(stars, 1, nameof(update));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(stars, 7, nameof(update));
        }

        bool improvesRelic = update.TargetRelicTier is int targetRelic && targetRelic > unit.RelicTier;
        bool improvesStars = update.TargetStars is int targetStars && targetStars > unit.Rarity;
        if (!improvesRelic && !improvesStars)
        {
            throw new ArgumentException("The target must improve the unit beyond its current roster state.", nameof(update));
        }
    }

    private static string NormalizeDefinitionId(string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        return definitionId.Trim();
    }

    private static void ValidateAllyCode(long allyCode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(allyCode, 100_000_000L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(allyCode, 999_999_999L);
    }
}
