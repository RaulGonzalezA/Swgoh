using MongoDB.Driver;

using RepositoryMongoDb.Repository;

using Swgoh.Application.Gac;
using Swgoh.Domain.Gac;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class GacTeamPresetMongoRepository(
    IMongoDbRepository<GacTeamPresetDocument, string> repository) : IGacTeamPresetRepository
{
    internal const string CollectionName = "gacTeamPresets";
    internal const string AllyCodeFormatIndexName = "ix_gac_team_presets_ally_format";

    public async Task<GacTeamPreset?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        GacTeamPresetDocument? document = await repository
            .FindByIdAsync(ToDocumentId(id), cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<IReadOnlyCollection<GacTeamPreset>> GetAsync(
        long allyCode,
        GacFormat? format,
        CancellationToken cancellationToken = default)
    {
        FilterDefinitionBuilder<GacTeamPresetDocument> builder = Builders<GacTeamPresetDocument>.Filter;
        FilterDefinition<GacTeamPresetDocument> filter = builder.Eq(document => document.AllyCode, allyCode);
        if (format is GacFormat requestedFormat)
        {
            filter &= builder.Eq(document => document.Format, (int)requestedFormat);
        }

        SortDefinition<GacTeamPresetDocument> sort = Builders<GacTeamPresetDocument>.Sort
            .Ascending(document => document.Use)
            .Ascending(document => document.Name);
        IReadOnlyCollection<GacTeamPresetDocument> documents = await repository
            .FindPageAsync(filter, skip: 0, limit: 500, sort, cancellationToken)
            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public Task UpsertAsync(GacTeamPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return repository.UpsertAsync(ToDocument(preset), cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        DeleteResult result = await repository
            .DeleteByIdAsync(ToDocumentId(id), cancellationToken)
            .ConfigureAwait(false);
        return result.DeletedCount > 0;
    }

    private static GacTeamPresetDocument ToDocument(GacTeamPreset preset) => new()
    {
        Id = ToDocumentId(preset.Id),
        AllyCode = preset.AllyCode,
        Name = preset.Name,
        Format = (int)preset.Format,
        Use = (int)preset.Use,
        Squad = ToDocument(preset.Squad),
        CreatedAtUtc = preset.CreatedAtUtc,
        UpdatedAtUtc = preset.UpdatedAtUtc
    };

    private static GacTeamPreset ToDomain(GacTeamPresetDocument document)
    {
        var format = (GacFormat)document.Format;
        return GacTeamPreset.Restore(
            Guid.ParseExact(document.Id, "D"),
            document.AllyCode,
            document.Name,
            format,
            (GacPlannerTeamUse)document.Use,
            ToDomain(document.Squad, format),
            document.CreatedAtUtc,
            document.UpdatedAtUtc);
    }

    private static GacPlannerSquadDocument ToDocument(GacPlannerSquad squad) => new()
    {
        LeaderDefinitionId = squad.LeaderDefinitionId,
        MemberDefinitionIds = [.. squad.MemberDefinitionIds],
        IsFleet = squad.IsFleet
    };

    private static GacPlannerSquad ToDomain(GacPlannerSquadDocument document, GacFormat format) =>
        GacPlannerSquad.Create(
            format,
            document.LeaderDefinitionId,
            document.MemberDefinitionIds,
            document.IsFleet);

    private static string ToDocumentId(Guid id) => id.ToString("D");
}

internal sealed class GacRoundPlanMongoRepository(
    IMongoDbRepository<GacRoundPlanDocument, string> repository) : IGacRoundPlanRepository
{
    internal const string CollectionName = "gacRoundPlans";
    internal const string PlayerUpdatedIndexName = "ix_gac_round_plans_player_updated";

    public async Task<GacRoundPlan?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        GacRoundPlanDocument? document = await repository
            .FindByIdAsync(id.Trim(), cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public Task UpsertAsync(GacRoundPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return repository.UpsertAsync(ToDocument(plan), cancellationToken);
    }

    private static GacRoundPlanDocument ToDocument(GacRoundPlan plan) => new()
    {
        Id = plan.Id,
        PlayerAllyCode = plan.PlayerAllyCode,
        OpponentAllyCode = plan.OpponentAllyCode,
        EventId = plan.EventId,
        EventInstanceId = plan.EventInstanceId,
        RoundNumber = plan.RoundNumber,
        Format = (int)plan.Format,
        League = (int)plan.League,
        OwnDefenses =
        [
            .. plan.OwnDefenses.Select(item => new GacOwnDefenseAssignmentDocument
            {
                Id = item.Id.ToString("D"),
                Zone = item.Zone,
                TeamPresetId = item.TeamPresetId.ToString("D")
            })
        ],
        VisibleDefenses =
        [
            .. plan.VisibleDefenses.Select(item => new GacVisibleDefenseDocument
            {
                Id = item.Id.ToString("D"),
                Zone = item.Zone,
                Label = item.Label,
                Squad = ToDocument(item.Squad)
            })
        ],
        Attacks =
        [
            .. plan.Attacks.Select(item => new GacAttackAssignmentDocument
            {
                Id = item.Id.ToString("D"),
                DefenseId = item.DefenseId.ToString("D"),
                TeamPresetId = item.TeamPresetId.ToString("D"),
                Attempt = item.Attempt,
                Status = (int)item.Status,
                Notes = item.Notes
            })
        ],
        CreatedAtUtc = plan.CreatedAtUtc,
        UpdatedAtUtc = plan.UpdatedAtUtc
    };

    private static GacRoundPlan ToDomain(GacRoundPlanDocument document)
    {
        var format = (GacFormat)document.Format;
        return GacRoundPlan.Restore(
            document.PlayerAllyCode,
            document.OpponentAllyCode,
            document.EventId,
            document.EventInstanceId,
            document.RoundNumber,
            format,
            (GacLeague)document.League,
            document.OwnDefenses.Select(item => GacOwnDefenseAssignment.Create(
                Guid.ParseExact(item.Id, "D"),
                item.Zone,
                Guid.ParseExact(item.TeamPresetId, "D"))),
            document.VisibleDefenses.Select(item => GacVisibleDefense.Create(
                Guid.ParseExact(item.Id, "D"),
                item.Zone,
                item.Label,
                ToDomain(item.Squad, format))),
            document.Attacks.Select(item => GacAttackAssignment.Create(
                Guid.ParseExact(item.Id, "D"),
                Guid.ParseExact(item.DefenseId, "D"),
                Guid.ParseExact(item.TeamPresetId, "D"),
                item.Attempt,
                (GacAttackPlanStatus)item.Status,
                item.Notes)),
            document.CreatedAtUtc,
            document.UpdatedAtUtc);
    }

    private static GacPlannerSquadDocument ToDocument(GacPlannerSquad squad) => new()
    {
        LeaderDefinitionId = squad.LeaderDefinitionId,
        MemberDefinitionIds = [.. squad.MemberDefinitionIds],
        IsFleet = squad.IsFleet
    };

    private static GacPlannerSquad ToDomain(GacPlannerSquadDocument document, GacFormat format) =>
        GacPlannerSquad.Create(
            format,
            document.LeaderDefinitionId,
            document.MemberDefinitionIds,
            document.IsFleet);
}
