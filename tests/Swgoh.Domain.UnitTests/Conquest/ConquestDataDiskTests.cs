using Swgoh.Domain.Conquest;

using Xunit;

namespace Swgoh.Domain.UnitTests.Conquest;

public sealed class ConquestDataDiskTests
{
    [Fact]
    public void ReplaceDataDisks_ValidLoadout_IsStored()
    {
        DateTimeOffset now = new(2026, 9, 13, 22, 0, 0, TimeSpan.Zero);
        Guid featId = Guid.NewGuid();
        ConquestPlan plan = CreatePlan(now, featId);
        ConquestDataDisk disk = ConquestDataDisk.Create(
            Guid.NewGuid(),
            "Jedi offense",
            3,
            5m,
            ConquestDataDiskTarget.Create(ConquestDataDiskTargetType.Faction, "JEDI", [], 3),
            [featId],
            "Helps the Jedi feat");
        ConquestDiskLoadout loadout = ConquestDiskLoadout.Create(
            Guid.NewGuid(),
            "Jedi feat",
            [disk.Id]);

        plan.ReplaceDataDisks(12, [disk], [loadout], now);

        Assert.Equal(12, plan.DiskCapacityLimit);
        Assert.Single(plan.DataDisks);
        Assert.Single(plan.DiskLoadouts);
        Assert.Equal(3, plan.GetLoadoutCapacity(loadout));
    }

    [Fact]
    public void ReplaceDataDisks_LoadoutOverCapacity_Throws()
    {
        DateTimeOffset now = new(2026, 9, 13, 22, 0, 0, TimeSpan.Zero);
        Guid featId = Guid.NewGuid();
        ConquestPlan plan = CreatePlan(now, featId);
        ConquestDataDisk disk = ConquestDataDisk.Create(
            Guid.NewGuid(),
            "Large disk",
            8,
            4m,
            ConquestDataDiskTarget.Create(ConquestDataDiskTargetType.AnyTeam, null, [], 1),
            [],
            null);
        ConquestDataDisk second = ConquestDataDisk.Create(
            Guid.NewGuid(),
            "Second disk",
            6,
            3m,
            ConquestDataDiskTarget.Create(ConquestDataDiskTargetType.AnyTeam, null, [], 1),
            [],
            null);
        ConquestDiskLoadout loadout = ConquestDiskLoadout.Create(
            Guid.NewGuid(),
            "Too large",
            [disk.Id, second.Id]);

        Assert.Throws<ArgumentException>(() => plan.ReplaceDataDisks(
            12,
            [disk, second],
            [loadout],
            now));
    }

    [Fact]
    public void ReplaceDataDisks_DiskReferencesMissingFeat_Throws()
    {
        DateTimeOffset now = new(2026, 9, 13, 22, 0, 0, TimeSpan.Zero);
        Guid featId = Guid.NewGuid();
        ConquestPlan plan = CreatePlan(now, featId);
        ConquestDataDisk disk = ConquestDataDisk.Create(
            Guid.NewGuid(),
            "Stale feat disk",
            2,
            2m,
            ConquestDataDiskTarget.Create(ConquestDataDiskTargetType.AnyTeam, null, [], 1),
            [Guid.NewGuid()],
            null);

        Assert.Throws<ArgumentException>(() => plan.ReplaceDataDisks(12, [disk], [], now));
    }

    private static ConquestPlan CreatePlan(DateTimeOffset now, Guid featId)
    {
        ConquestFeat feat = ConquestFeat.Create(
            featId,
            "Jedi wins",
            ConquestFeatScope.Sector,
            1,
            10,
            5,
            0,
            1,
            ConquestFeatRule.Create(ConquestFeatRuleType.Faction, "JEDI", [], 3));
        return ConquestPlan.Create(
            123_456_789,
            "conquest-disks",
            "Conquista",
            ConquestDifficulty.Hard,
            [feat],
            now);
    }
}
