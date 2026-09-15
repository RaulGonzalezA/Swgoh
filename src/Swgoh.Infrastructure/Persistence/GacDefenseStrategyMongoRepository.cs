using RepositoryMongoDb.Repository;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class GacDefenseStrategyMongoRepository(
    IMongoDbRepository<GacDefenseStrategyDocument, string> repository) : IGacDefenseStrategyRepository
{
    internal const string CollectionName = "gacDefenseStrategies";

    public async Task<GacDefenseStrategyProfile?> FindAsync(
        long allyCode,
        GacFormat format,
        CancellationToken cancellationToken = default)
    {
        GacDefenseStrategyDocument? document = await repository
            .FindByIdAsync(BuildId(allyCode, format), cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public Task UpsertAsync(
        GacDefenseStrategyProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return repository.UpsertAsync(ToDocument(profile), cancellationToken);
    }

    private static GacDefenseStrategyDocument ToDocument(GacDefenseStrategyProfile profile) => new()
    {
        Id = BuildId(profile.AllyCode, profile.Format),
        AllyCode = profile.AllyCode,
        Format = (int)profile.Format,
        Slots =
        [
            .. profile.Slots.Select(slot => new GacDefenseTemplateSlotDocument
            {
                Position = slot.Position,
                Zone = slot.Zone,
                PinnedTeamPresetId = slot.PinnedTeamPresetId?.ToString("D")
            })
        ],
        ReservedAttackPresetIds = [.. profile.ReservedAttackPresetIds.Select(id => id.ToString("D"))],
        UpdatedAtUtc = profile.UpdatedAtUtc
    };

    private static GacDefenseStrategyProfile ToDomain(GacDefenseStrategyDocument document) => new(
        document.AllyCode,
        (GacFormat)document.Format,
        [
            .. document.Slots.Select(slot => new GacDefenseTemplateSlot(
                slot.Position,
                slot.Zone,
                string.IsNullOrWhiteSpace(slot.PinnedTeamPresetId)
                    ? null
                    : Guid.ParseExact(slot.PinnedTeamPresetId, "D")))
        ],
        [.. document.ReservedAttackPresetIds.Select(id => Guid.ParseExact(id, "D"))],
        document.UpdatedAtUtc);

    private static string BuildId(long allyCode, GacFormat format) => $"{allyCode}:{(int)format}";
}
