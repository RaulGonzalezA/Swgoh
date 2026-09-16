namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireCatalog
{
    public const string Version = "2026-09-16";

    private static readonly RiseOfEmpireMissionDefinition ZeffoUnlock = UnitMission(
        "Desbloqueo de Zeffo desde Bracca",
        "Special Unlock",
        "Cere Junda + Cal Kestis o Jedi Knight Cal Kestis a R7+",
        7,
        [Unit("Cere Junda", "CEREJUNDA"), Unit("Cal Kestis o Jedi Knight Cal Kestis", "CALKESTIS", "JEDIKNIGHTCAL")]);

    private static readonly RiseOfEmpireMissionDefinition MandaloreUnlock = CombinedMission(
        "Desbloqueo de Mandalore desde Tatooine",
        "Special Unlock",
        "Bo-Katan (Mand'alor) + The Mandalorian (Beskar Armor) + otro Mandaloriano, todos R7+",
        7,
        "mandalor",
        3,
        [Unit("Bo-Katan (Mand'alor)", "BOKATANMANDALORE"), Unit("The Mandalorian (Beskar Armor)", "THEMANDALORIANBESKARARMOR")]);

    public static IReadOnlyCollection<RiseOfEmpirePlanetDefinition> Planets { get; } =
    [
        Planet(1, "mustafar", "Mustafar", "Dark Side", 5, false, [116_406_250, 186_250_000, 248_333_333],
            [Dark("Imperio", "empire"), Dark("Sith", "sith"), Dark("Inquisitorius", "inquisitor")]),
        Planet(1, "corellia", "Corellia", "Mixed", 5, false, [111_718_750, 178_750_000, 238_333_333],
            [Mixed("Hutt Cartel", "hutt"), Mixed("Mandalorianos", "mandalor"), Mixed("Cazarrecompensas", "bounty")]),
        Planet(1, "coruscant", "Coruscant", "Light Side", 5, false, [116_406_250, 186_250_000, 248_333_333],
            [Light("Jedi", "jedi"), Light("República Galáctica", "galactic_republic"), Light("Rebeldes", "rebel")]),

        Planet(2, "geonosis", "Geonosis", "Dark Side", 6, false, [148_125_000, 237_000_000, 316_000_000],
            [Dark("Geonosianos", "geonosian"), Dark("Separatistas", "separatist"), Dark("Sith", "sith")],
            [FactionMission("Combate Geonosiano", "Combat", "5 Geonosianos", 6, "geonosian", 5)]),
        Planet(2, "felucia", "Felucia", "Mixed", 6, false, [148_125_000, 237_000_000, 316_000_000],
            [Mixed("Hutt Cartel", "hutt"), Mixed("Mandalorianos", "mandalor"), Mixed("Jedi", "jedi")]),
        Planet(2, "bracca", "Bracca", "Light Side", 6, false, [142_265_625, 227_625_000, 303_500_000],
            [Light("Jedi", "jedi"), Light("República Galáctica", "galactic_republic"), Light("Rebeldes", "rebel")],
            [FactionMission("Combate Jedi", "Combat", "Jedi R6+", 6, "jedi", 5), ZeffoUnlock]),

        Planet(3, "dathomir", "Dathomir", "Dark Side", 7, false, [158_960_938, 254_337_500, 339_116_667],
            [Dark("Imperio", "empire"), Dark("Hermanas de la Noche", "nightsister"), Dark("Sith", "sith")],
            [FactionMission("Combate Imperio", "Combat", "5 unidades del Imperio", 7, "empire", 5),
             UnitMission("Doctor Aphra", "Combat", "Doctor Aphra", 7, [Unit("Doctor Aphra", "DOCTORAPHRA")]),
             CombinedMission("Merrin", "Special", "Hermanas de la Noche + Merrin", 7, "nightsister", 5,
                [Unit("Merrin", "MERRIN")])]),
        Planet(3, "tatooine", "Tatooine", "Mixed", 7, false, [190_953_125, 305_525_000, 407_366_667],
            [Mixed("Inquisitorius", "inquisitor"), Mixed("Hutt Cartel", "hutt"), Mixed("Mandalorianos", "mandalor")],
            [FactionMission("Third Sister / Reva", "Special Unlock", "5 Inquisitorius R7+", 7, "inquisitor", 5), MandaloreUnlock]),
        Planet(3, "kashyyyk", "Kashyyyk", "Light Side", 7, false, [190_953_125, 305_525_000, 407_366_667],
            [Light("Wookiees", "wookie"), Light("Jedi", "jedi"), Light("República Galáctica", "galactic_republic")],
            [FactionMission("Combate Wookiee", "Combat", "5 Wookiees", 7, "wookie", 5)]),
        Planet(3, "zeffo", "Zeffo", "Light Side", 7, true, [143_589_583, 229_743_333, 287_179_167],
            [Light("Usuarios de la Fuerza no alineados", "unaligned_force_user"), Light("Jedi", "jedi")],
            [UnitMission("Jedi Knight Cal Kestis", "Combat", "Jedi Knight Cal Kestis", 7,
                [Unit("Jedi Knight Cal Kestis", "JEDIKNIGHTCAL")]),
             FactionMission("Clone Trooper", "Special", "Clone Troopers R7+", 7, "clone_trooper", 5)],
            ZeffoUnlock),

        Planet(4, "medical-station", "Medical Station", "Dark Side", 8, false, [235_143_105, 400_243_583, 500_304_479],
            [Dark("Inquisitorius", "inquisitor"), Dark("Imperio", "empire"), Dark("Sith", "sith")],
            [CombinedMission("Third Sister", "Special", "Inquisitorius + Third Sister", 8, "inquisitor", 5,
                [Unit("Third Sister", "THIRDSISTER")])]),
        Planet(4, "kessel", "Kessel", "Mixed", 8, false, [235_143_105, 400_243_583, 500_304_479],
            [Mixed("Hutt Cartel", "hutt"), Mixed("Contrabandistas", "smuggler"), Mixed("Cazarrecompensas", "bounty")]),
        Planet(4, "lothal", "Lothal", "Light Side", 8, false, [246_742_558, 419_987_333, 524_984_167],
            [Light("Phoenix", "phoenix"), Light("Rebeldes", "rebel"), Light("Jedi", "jedi")]),
        Planet(4, "mandalore", "Mandalore", "Mixed", 8, true, [197_748_650, 316_397_840, 396_497_300],
            [Mixed("Mandalorianos", "mandalor")],
            [],
            MandaloreUnlock),

        Planet(5, "malachor", "Malachor", "Dark Side", 9, false, [341_250_768, 620_455_942, 729_948_167],
            [Dark("Inquisitorius", "inquisitor"), Dark("Sith", "sith"), Dark("Imperio", "empire")],
            [UnitMission("Trío Inquisitorius", "Combat", "Eighth Brother + Fifth Brother + Seventh Sister", 9,
                [Unit("Eighth Brother", "EIGHTHBROTHER"), Unit("Fifth Brother", "FIFTHBROTHER"), Unit("Seventh Sister", "SEVENTHSISTER")])]),
        Planet(5, "vandor", "Vandor", "Mixed", 9, false, [341_250_768, 620_455_942, 729_948_167],
            [Mixed("Hutt Cartel", "hutt"), Mixed("Contrabandistas", "smuggler"), Mixed("Canallas", "scoundrel")],
            [UnitMission("Jabba", "Combat", "Jabba the Hutt", 9, [Unit("Jabba the Hutt", "JABBATHEHUTT")]),
             UnitMission("Young Han + Vandor Chewbacca", "Special", "Young Han Solo + Vandor Chewbacca", 9,
                [Unit("Young Han Solo", "YOUNGHAN"), Unit("Vandor Chewbacca", "VANDORCHEWBACCA")])]),
        Planet(5, "ring-of-kafrene", "Ring of Kafrene", "Light Side", 9, false, [341_250_768, 620_455_942, 729_948_167],
            [Light("Rebeldes", "rebel"), Light("Rogue One", "rogue_one"), Light("Jedi", "jedi")],
            [UnitMission("Cassian + K-2SO", "Combat", "Cassian Andor + K-2SO", 9,
                [Unit("Cassian Andor", "CASSIANANDOR"), Unit("K-2SO", "K2SO")])]),

        Planet(6, "death-star", "Death Star", "Dark Side", 9, false, [582_632_425, 1_059_331_682, 1_246_272_567],
            [Dark("Imperio", "empire"), Dark("Soldados Imperiales", "imperial_trooper"), Dark("Sith", "sith")],
            [UnitMission("Iden Versio", "Combat", "Iden Versio", 9, [Unit("Iden Versio", "IDENVERSIO")]),
             UnitMission("Darth Vader", "Combat", "Darth Vader", 9, [Unit("Darth Vader", "DARTHVADER")])]),
        Planet(6, "hoth", "Hoth", "Mixed", 9, false, [582_632_425, 1_059_331_682, 1_246_272_567],
            [Mixed("Rebeldes", "rebel"), Mixed("Imperio", "empire"), Mixed("Jedi", "jedi")]),
        Planet(6, "scarif", "Scarif", "Light Side", 9, false, [555_710_999, 1_010_383_635, 1_188_686_629],
            [Light("Rogue One", "rogue_one"), Light("Rebeldes", "rebel"), Light("Jedi", "jedi")])
    ];

    private static RiseOfEmpirePlanetDefinition Planet(
        int phase,
        string id,
        string name,
        string alignment,
        int minimumRelicTier,
        bool isBonusZone,
        IReadOnlyCollection<long> starThresholds,
        IReadOnlyCollection<RiseOfEmpireArchetypeDefinition> archetypes,
        IReadOnlyCollection<RiseOfEmpireMissionDefinition>? missions = null,
        RiseOfEmpireMissionDefinition? accessRequirement = null) =>
        new(phase, id, name, alignment, minimumRelicTier, isBonusZone, starThresholds, archetypes, missions ?? [], accessRequirement);

    private static RiseOfEmpireArchetypeDefinition Dark(string name, string tag) => new(name, tag);
    private static RiseOfEmpireArchetypeDefinition Light(string name, string tag) => new(name, tag);
    private static RiseOfEmpireArchetypeDefinition Mixed(string name, string tag) => new(name, tag);

    private static RiseOfEmpireMissionDefinition FactionMission(
        string name, string type, string requirement, int relic, string tag, int units) =>
        new(name, type, requirement, relic, tag, units, []);

    private static RiseOfEmpireMissionDefinition UnitMission(
        string name,
        string type,
        string requirement,
        int relic,
        IReadOnlyCollection<RiseOfEmpireUnitRequirementDefinition> units) =>
        new(name, type, requirement, relic, null, 0, units);

    private static RiseOfEmpireMissionDefinition CombinedMission(
        string name,
        string type,
        string requirement,
        int relic,
        string tag,
        int minimumFactionUnits,
        IReadOnlyCollection<RiseOfEmpireUnitRequirementDefinition> units) =>
        new(name, type, requirement, relic, tag, minimumFactionUnits, units);

    private static RiseOfEmpireUnitRequirementDefinition Unit(string label, params string[] aliases) =>
        new(label, aliases);
}

internal sealed record RiseOfEmpirePlanetDefinition(
    int Phase,
    string Id,
    string Name,
    string Alignment,
    int MinimumRelicTier,
    bool IsBonusZone,
    IReadOnlyCollection<long> StarThresholds,
    IReadOnlyCollection<RiseOfEmpireArchetypeDefinition> Archetypes,
    IReadOnlyCollection<RiseOfEmpireMissionDefinition> Missions,
    RiseOfEmpireMissionDefinition? AccessRequirement = null);

internal sealed record RiseOfEmpireArchetypeDefinition(string Name, string TagKeyword);

internal sealed record RiseOfEmpireMissionDefinition(
    string Name,
    string Type,
    string Requirement,
    int MinimumRelicTier,
    string? FactionTagKeyword,
    int MinimumFactionUnits,
    IReadOnlyCollection<RiseOfEmpireUnitRequirementDefinition> UnitRequirements);

internal sealed record RiseOfEmpireUnitRequirementDefinition(
    string Label,
    IReadOnlyCollection<string> Aliases);
