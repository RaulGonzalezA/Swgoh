using RepositoryMongoDb.Repository;

using Swgoh.Application.Players;
using Swgoh.Domain.Players;
using Swgoh.Infrastructure.Persistence.Documents;

namespace Swgoh.Infrastructure.Persistence;

internal sealed class PlayerMongoRepository(IMongoDbRepository<PlayerDocument, long> repository) : IPlayerRepository
{
    internal const string CollectionName = "players";

    public async Task<PlayerProfile?> FindByAllyCodeAsync(long allyCode, CancellationToken cancellationToken = default)
    {
        PlayerDocument? document = await repository.FindByIdAsync(allyCode, cancellationToken).ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public Task UpsertAsync(PlayerProfile player, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        return repository.UpsertAsync(ToDocument(player), cancellationToken);
    }

    private static PlayerDocument ToDocument(PlayerProfile player) => new()
    {
        Id = player.AllyCode,
        AllyCode = player.AllyCode,
        PlayerId = player.PlayerId,
        Name = player.Name,
        GuildId = player.GuildId,
        GuildName = player.GuildName,
        Level = player.Level,
        GalacticPower = player.GalacticPower,
        UpdatedAtUtc = player.UpdatedAtUtc,
        Roster =
        [
            .. player.Roster.Select(unit => new RosterUnitDocument
            {
                Id = unit.Id,
                DefinitionId = unit.DefinitionId,
                Level = unit.Level,
                Rarity = unit.Rarity,
                GearTier = unit.GearTier,
                RelicTier = unit.RelicTier,
                EquippedModCount = unit.EquippedModCount,
                GalacticPower = unit.GalacticPower,
                IsShip = unit.IsShip,
                ZetaCount = unit.ZetaCount,
                OmicronCount = unit.OmicronCount,
                Stats = ToDocument(unit.Stats),
                Mods = ToDocument(unit.Mods)
            })
        ],
        Datacrons =
        [
            .. player.Datacrons.Select(datacron => new PlayerDatacronDocument
            {
                Id = datacron.Id,
                SetId = datacron.SetId,
                TemplateId = datacron.TemplateId,
                Tier = datacron.Tier,
                Locked = datacron.Locked,
                Affixes =
                [
                    .. datacron.Affixes.Select(affix => new PlayerDatacronAffixDocument
                    {
                        AbilityId = affix.AbilityId,
                        StatType = affix.StatType,
                        StatValue = affix.StatValue,
                        RequiredRelicTier = affix.RequiredRelicTier,
                        Tags = [.. affix.Tags]
                    })
                ]
            })
        ]
    };

    private static PlayerProfile ToDomain(PlayerDocument document) => PlayerProfile.Import(
        document.AllyCode,
        document.PlayerId,
        document.Name,
        document.GuildId,
        document.GuildName,
        document.Level,
        document.GalacticPower,
        document.UpdatedAtUtc,
        document.Roster.Select(unit => new RosterUnit(
            unit.Id,
            unit.DefinitionId,
            unit.Level,
            unit.Rarity,
            unit.GearTier,
            unit.RelicTier,
            unit.EquippedModCount,
            unit.GalacticPower,
            unit.IsShip,
            unit.ZetaCount,
            unit.OmicronCount,
            ToDomain(unit.Stats),
            ToDomain(unit.Mods))),
        (document.Datacrons ?? []).Select(datacron => new PlayerDatacron(
            datacron.Id,
            datacron.SetId,
            datacron.TemplateId,
            datacron.Tier,
            datacron.Locked,
            [
                .. (datacron.Affixes ?? []).Select(affix => new PlayerDatacronAffix(
                    affix.AbilityId,
                    affix.StatType,
                    affix.StatValue,
                    affix.RequiredRelicTier,
                    affix.Tags ?? []))
            ])));

    private static RosterUnitStatsDocument? ToDocument(RosterUnitStats? stats) => stats is null
        ? null
        : new RosterUnitStatsDocument
        {
            Health = stats.Health,
            Protection = stats.Protection,
            Speed = stats.Speed,
            PhysicalDamage = stats.PhysicalDamage,
            SpecialDamage = stats.SpecialDamage,
            Armor = stats.Armor,
            Resistance = stats.Resistance,
            Potency = stats.Potency,
            Tenacity = stats.Tenacity,
            CriticalDamage = stats.CriticalDamage
        };

    private static RosterModSummaryDocument? ToDocument(RosterModSummary? mods) => mods is null
        ? null
        : new RosterModSummaryDocument
        {
            EquippedCount = mods.EquippedCount,
            SixDotCount = mods.SixDotCount,
            SpeedSetModCount = mods.SpeedSetModCount,
            SpeedPrimaryCount = mods.SpeedPrimaryCount,
            SpeedBonus = mods.SpeedBonus
        };

    private static RosterUnitStats? ToDomain(RosterUnitStatsDocument? stats) => stats is null
        ? null
        : new RosterUnitStats(
            stats.Health,
            stats.Protection,
            stats.Speed,
            stats.PhysicalDamage,
            stats.SpecialDamage,
            stats.Armor,
            stats.Resistance,
            stats.Potency,
            stats.Tenacity,
            stats.CriticalDamage);

    private static RosterModSummary? ToDomain(RosterModSummaryDocument? mods) => mods is null
        ? null
        : new RosterModSummary(
            mods.EquippedCount,
            mods.SixDotCount,
            mods.SpeedSetModCount,
            mods.SpeedPrimaryCount,
            mods.SpeedBonus);
}
