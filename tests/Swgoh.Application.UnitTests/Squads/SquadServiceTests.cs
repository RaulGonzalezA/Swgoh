using Swgoh.Application.Abstractions;
using Swgoh.Application.GameData;
using Swgoh.Application.Squads;
using Swgoh.Domain.Squads;

using Xunit;

namespace Swgoh.Application.UnitTests.Squads;

public sealed class SquadServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidatesGameDataPersistsAndReturnsEnrichedUnits()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var repository = new FakeSquadRepository();
        var service = new SquadService(repository, new FakeGameDataCatalog(CreateCatalog()), new FixedClock());
        var input = new SaveSquadDefinition(
            "Equipo Sith",
            SquadFormat.ThreeVsThree,
            SquadUse.Defense,
            [" GAC ", "SITH"],
            [new SquadVariantInput("default", "Principal", "leader", ["member1", "MEMBER2"])]);

        SquadDetails result = await service.CreateAsync(input, cancellationToken);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Equipo Sith", result.Name);
        Assert.Equal(["gac", "sith"], result.Tags);
        SquadVariantDetails variant = Assert.Single(result.Variants);
        Assert.Equal("LEADER", variant.Leader.DefinitionId);
        Assert.Equal("Líder", variant.Leader.Name);
        Assert.Equal("tex.leader", variant.Leader.ThumbnailName);
        Assert.Equal(2, variant.Members.Count);
        Assert.Equal(1, repository.UpsertCount);
        Assert.NotNull(await repository.FindByIdAsync(result.Id, cancellationToken));
    }

    [Theory]
    [InlineData("UNKNOWN", "Unknown SWGOH unit")]
    [InlineData("SHIP", "cannot be used")]
    public async Task CreateAsync_WithInvalidUnit_RejectsBeforePersistence(string definitionId, string expectedMessage)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var repository = new FakeSquadRepository();
        var service = new SquadService(repository, new FakeGameDataCatalog(CreateCatalog()), new FixedClock());
        var input = new SaveSquadDefinition(
            "Invalid",
            SquadFormat.ThreeVsThree,
            SquadUse.Flexible,
            [],
            [new SquadVariantInput("default", "Principal", definitionId, ["MEMBER1", "MEMBER2"])]);

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(input, cancellationToken));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, repository.UpsertCount);
    }

    [Fact]
    public async Task UpdateSearchAndDeleteAsync_UsesPersistedAggregateAndNormalizedQuery()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var repository = new FakeSquadRepository();
        var service = new SquadService(repository, new FakeGameDataCatalog(CreateCatalog()), new FixedClock());
        SquadDetails created = await service.CreateAsync(
            new SaveSquadDefinition(
                "Defensa",
                SquadFormat.ThreeVsThree,
                SquadUse.Defense,
                ["sith"],
                [new SquadVariantInput("default", "Principal", "LEADER", ["MEMBER1", "MEMBER2"])]),
            cancellationToken);

        SquadDetails? updated = await service.UpdateAsync(
            created.Id,
            new SaveSquadDefinition(
                "Ataque",
                SquadFormat.FiveVsFive,
                SquadUse.Offense,
                ["gac"],
                [new SquadVariantInput(
                    "default",
                    "Principal",
                    "LEADER",
                    ["MEMBER1", "MEMBER2", "MEMBER3", "MEMBER4"])]),
            cancellationToken);
        IReadOnlyCollection<SquadDetails> results = await service.SearchAsync(
            new SquadSearchQuery("  Ata  ", SquadFormat.FiveVsFive, SquadUse.Offense, " GAC ", 25),
            cancellationToken);
        bool deleted = await service.DeleteAsync(created.Id, cancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(SquadFormat.FiveVsFive, updated.Format);
        Assert.Single(results);
        Assert.Equal("Ata", repository.LastQuery?.Search);
        Assert.Equal("gac", repository.LastQuery?.Tag);
        Assert.True(deleted);
        Assert.Null(await service.GetAsync(created.Id, cancellationToken));
    }

    private static GameDataCatalog CreateCatalog() => new(
        new Dictionary<string, GameUnitDefinition>(StringComparer.Ordinal)
        {
            ["LEADER"] = new("LEADER", false, null, "Líder", "tex.leader", ["Sith"], ["affiliation_sith"]),
            ["MEMBER1"] = new("MEMBER1", false, null, "Miembro 1", "tex.member1", ["Sith"], ["affiliation_sith"]),
            ["MEMBER2"] = new("MEMBER2", false, null, "Miembro 2", "tex.member2", ["Sith"], ["affiliation_sith"]),
            ["MEMBER3"] = new("MEMBER3", false, null, "Miembro 3", null, ["Sith"], ["affiliation_sith"]),
            ["MEMBER4"] = new("MEMBER4", false, null, "Miembro 4", null, ["Sith"], ["affiliation_sith"]),
            ["SHIP"] = new("SHIP", true, null, "Nave", null, ["Sith"], ["affiliation_sith"])
        },
        new Dictionary<string, GameSkillDefinition>(StringComparer.Ordinal),
        []);

    private sealed class FakeGameDataCatalog(GameDataCatalog catalog) : ISwgohGameDataCatalog
    {
        public Task<GameDataCatalog> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(catalog);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-09-13T08:00:00Z");
    }

    private sealed class FakeSquadRepository : ISquadRepository
    {
        private readonly Dictionary<Guid, SquadDefinition> squads = [];

        public int UpsertCount { get; private set; }
        public SquadSearchQuery? LastQuery { get; private set; }

        public Task<SquadDefinition?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            squads.TryGetValue(id, out SquadDefinition? squad);
            return Task.FromResult(squad);
        }

        public Task<IReadOnlyCollection<SquadDefinition>> SearchAsync(
            SquadSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            IEnumerable<SquadDefinition> result = squads.Values;
            if (query.Format is SquadFormat format)
            {
                result = result.Where(squad => squad.Format == format);
            }

            if (query.Use is SquadUse use)
            {
                result = result.Where(squad => squad.Use == use);
            }

            if (!string.IsNullOrWhiteSpace(query.Tag))
            {
                result = result.Where(squad => squad.Tags.Contains(query.Tag, StringComparer.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                result = result.Where(squad => squad.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyCollection<SquadDefinition>>([.. result.Take(query.Limit)]);
        }

        public Task UpsertAsync(SquadDefinition squad, CancellationToken cancellationToken = default)
        {
            squads[squad.Id] = squad;
            UpsertCount++;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(squads.Remove(id));
    }
}
