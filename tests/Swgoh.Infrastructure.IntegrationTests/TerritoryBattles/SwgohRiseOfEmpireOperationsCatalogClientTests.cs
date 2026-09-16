using System.Text.Json;

using Swgoh.Application.TerritoryBattles;
using Swgoh.Infrastructure.TerritoryBattles;

using Xunit;

namespace Swgoh.Infrastructure.IntegrationTests.TerritoryBattles;

public sealed class SwgohRiseOfEmpireOperationsCatalogClientTests
{
    [Fact]
    public void Parse_NormalizesPhasePlanetAndInternalRelicTier()
    {
        const string json = """
            [
              {
                "id": "P1-C1",
                "phase": "P1",
                "type": "LS",
                "bonus": false,
                "nameKey": "Coruscant Operation",
                "totalPoints": 10000000,
                "squads": [
                  {
                    "id": "tb3-platoon-1",
                    "points": 10000000,
                    "units": [
                      { "baseId": "GENERALSKYWALKER", "nameKey": "General Skywalker", "combatType": 1, "rarity": 7, "unitRelicTier": 7 },
                      { "baseId": "PHANTOM2", "nameKey": "Phantom II", "combatType": 2, "rarity": 7, "unitRelicTier": 7 }
                    ]
                  }
                ]
              }
            ]
            """;
        using JsonDocument document = JsonDocument.Parse(json);

        RiseOfEmpireOperationDefinition operation = Assert.Single(
            SwgohRiseOfEmpireOperationsCatalogClient.Parse(document.RootElement));
        RiseOfEmpireOperationSquadDefinition squad = Assert.Single(operation.Squads);
        RiseOfEmpireOperationUnitDefinition character = Assert.Single(squad.Units, unit => !unit.IsShip);
        RiseOfEmpireOperationUnitDefinition ship = Assert.Single(squad.Units, unit => unit.IsShip);

        Assert.Equal(1, operation.Phase);
        Assert.Equal("Coruscant", operation.PlanetName);
        Assert.Equal(5, character.RequiredRelicTier);
        Assert.Equal(0, ship.RequiredRelicTier);
        Assert.Equal(7, ship.RequiredRarity);
    }
}
