using Swgoh.Application.GameData;
using Swgoh.Application.Players;
using Swgoh.Domain.Players;

using Xunit;

namespace Swgoh.Application.UnitTests.Players;

public sealed class GalacticLegendProgressServiceTests
{
    [Fact]
    public async Task GetAsync_CalculatesOwnedCompletedAndPercentage()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        PlayerProfile player = PlayerProfile.Import(
            476_825_771,
            "player-id",
            "Aberronko",
            null,
            null,
            85,
            1_000_000,
            DateTimeOffset.UtcNow,
            [
                new RosterUnit("gl", "GL_TEST", 85, 7, 13, 9, 6),
                new RosterUnit("a", "REQ_A", 85, 7, 13, 7, 6),
                new RosterUnit("b", "REQ_B", 85, 7, 13, 4, 6)
            ]);
        var repository = new FakePlayerRepository(player);
        var catalog = new FakeGameDataCatalog(new GameDataCatalog(
            new Dictionary<string, GameUnitDefinition>(),
            new Dictionary<string, GameSkillDefinition>(),
            [
                new GalacticLegendDefinition(
                    "GL_TEST",
                    "requirement-test",
                    [
                        new GalacticLegendUnitRequirement("REQ_A", 7, 13, 7),
                        new GalacticLegendUnitRequirement("REQ_B", 7, 13, 5),
                        new GalacticLegendUnitRequirement("REQ_C", 7, 13, 3)
                    ])
            ]));
        var service = new GalacticLegendProgressService(repository, catalog);

        IReadOnlyCollection<GalacticLegendProgress>? result = await service.GetAsync(476_825_771, cancellationToken);

        GalacticLegendProgress progress = Assert.Single(Assert.IsAssignableFrom<IReadOnlyCollection<GalacticLegendProgress>>(result));
        Assert.True(progress.Unlocked);
        Assert.Equal(1, progress.CompletedRequirements);
        Assert.Equal(3, progress.TotalRequirements);
        Assert.Equal(33.3m, progress.CompletionPercent);

        GalacticLegendRequirementProgress reqA = Assert.Single(progress.Requirements, requirement => requirement.UnitBaseId == "REQ_A");
        Assert.True(reqA.Owned);
        Assert.True(reqA.Complete);

        GalacticLegendRequirementProgress reqB = Assert.Single(progress.Requirements, requirement => requirement.UnitBaseId == "REQ_B");
        Assert.True(reqB.Owned);
        Assert.False(reqB.Complete);
        Assert.Equal(4, reqB.CurrentRelicTier);

        GalacticLegendRequirementProgress reqC = Assert.Single(progress.Requirements, requirement => requirement.UnitBaseId == "REQ_C");
        Assert.False(reqC.Owned);
        Assert.False(reqC.Complete);
    }

    [Fact]
    public async Task GetAsync_WhenPlayerDoesNotExist_ReturnsNull()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var service = new GalacticLegendProgressService(
            new FakePlayerRepository(null),
            new FakeGameDataCatalog(new GameDataCatalog(
                new Dictionary<string, GameUnitDefinition>(),
                new Dictionary<string, GameSkillDefinition>(),
                [])));

        IReadOnlyCollection<GalacticLegendProgress>? result = await service.GetAsync(476_825_771, cancellationToken);

        Assert.Null(result);
    }

    private sealed class FakePlayerRepository(PlayerProfile? player) : IPlayerRepository
    {
        public Task<PlayerProfile?> FindByAllyCodeAsync(long allyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(player);

        public Task UpsertAsync(PlayerProfile playerProfile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeGameDataCatalog(GameDataCatalog catalog) : ISwgohGameDataCatalog
    {
        public Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(catalog);
    }
}
