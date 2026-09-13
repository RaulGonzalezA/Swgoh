using Swgoh.Domain.Squads;

namespace Swgoh.Application.Squads;

public sealed record SquadVariantInput(
    string Key,
    string Name,
    string LeaderDefinitionId,
    IReadOnlyCollection<string> MemberDefinitionIds);

public sealed record SaveSquadDefinition(
    string Name,
    SquadFormat Format,
    SquadUse Use,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<SquadVariantInput> Variants);

public sealed record SquadSearchQuery(
    string? Search = null,
    SquadFormat? Format = null,
    SquadUse? Use = null,
    string? Tag = null,
    int Limit = 100);

public sealed record SquadUnitDetails(
    string DefinitionId,
    string Name,
    string? ThumbnailName,
    IReadOnlyCollection<string> Factions);

public sealed record SquadVariantDetails(
    string Key,
    string Name,
    SquadUnitDetails Leader,
    IReadOnlyCollection<SquadUnitDetails> Members);

public sealed record SquadDetails(
    Guid Id,
    string Name,
    SquadFormat Format,
    SquadUse Use,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<SquadVariantDetails> Variants,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
