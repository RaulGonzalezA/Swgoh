namespace Swgoh.Application.Eras;

internal static class EraCatalog
{
    public const string Version = "2026-09-16.1";
    public const string EraId = "myths-and-legends";
    public const string EraName = "Era of Myths & Legends";
    public static readonly DateOnly StartedOn = new(2026, 7, 28);

    public static IReadOnlyCollection<EraUnitDefinition> Units { get; } =
    [
        new(
            "mara-jade-skywalker",
            "Mara Jade Skywalker",
            "Light Side",
            "Attacker",
            ["Jedi", "New Republic"],
            false,
            ["Mara Jade Skywalker"],
            "Jedi Knight Luke Skywalker",
            "Su kit está diseñado para funcionar en tándem con Jedi Knight Luke y potenciar ataques fuera de turno."),
        new(
            "yoda-dark-side-vision",
            "Yoda (Dark Side Vision)",
            "Dark Side",
            "Healer",
            ["Sith"],
            false,
            ["Yoda (Dark Side Vision)", "Yoda Dark Side Vision"],
            "Sith / Darth Jar Jar",
            "Soporte Sith con Dark Master's Training; su kit de Territory Battles refuerza equipos Sith."),
        new(
            "starkiller-luke-concept",
            "Starkiller (Luke Concept)",
            "Light Side",
            "Support",
            ["Rebel", "Unaligned Force User"],
            false,
            ["Starkiller (Luke Concept)", "Starkiller Luke Concept"],
            "Commander Luke Skywalker",
            "Refuerza equipos CLS con control de Turn Meter, Expose y Shared Fate."),
        new(
            "stormtrooper-concept",
            "Stormtrooper (Concept)",
            "Dark Side",
            "Tank",
            ["Empire", "Imperial Trooper"],
            false,
            ["Stormtrooper (Concept)", "Stormtrooper Concept"],
            "General Veers / Imperial Troopers",
            "Tanque para Troopers; aporta Taunt, recuperación de Protección, assists y Turn Meter en GAC."),
        new(
            "jaxxon",
            "Jaxxon",
            "Light Side",
            "Attacker",
            ["Mercenary", "Scoundrel", "Smuggler"],
            false,
            ["Jaxxon"],
            "Dash Rendar / Prepared",
            "Atacante Prepared con mucha generación de Turn Meter y sinergia especial con versiones de Lando."),
        new(
            "the-ronin",
            "The Ronin",
            "Dark Side",
            "Attacker",
            ["Sith"],
            false,
            ["The Ronin", "Ronin"],
            "Darth Jar Jar / Sith",
            "Atacante Sith que invoca a R5-D56 y escala especialmente con Darth Jar Jar y un Sith Healer."),
        new(
            "darth-jar-jar",
            "Darth Jar Jar",
            "Dark Side",
            "Leader / Support",
            ["Gungan", "Sith"],
            true,
            ["Darth Jar Jar"],
            "The Ronin / Yoda (Dark Side Vision)",
            "Unidad de Journey Guide de la Era; líder Gungan Sith para el núcleo de Ronin y Yoda oscuro.")
    ];

    public static IReadOnlyCollection<EraJourneyTierDefinition> DarthJarJarJourney { get; } =
    [
        new(
            1,
            4,
            ["Mara Jade Skywalker", "Yoda (Dark Side Vision)", "Starkiller (Luke Concept)", "Stormtrooper (Concept)", "Jaxxon"],
            [new("Yoda (Dark Side Vision)", 90)],
            "80 fragmentos de Darth Jar Jar + materiales de Era"),
        new(
            2,
            5,
            ["Mara Jade Skywalker", "Yoda (Dark Side Vision)", "Starkiller (Luke Concept)", "Stormtrooper (Concept)", "Jaxxon"],
            [new("Starkiller (Luke Concept)", 95)],
            "65 fragmentos de Darth Jar Jar + materiales de Era"),
        new(
            3,
            6,
            ["Mara Jade Skywalker", "Yoda (Dark Side Vision)", "Starkiller (Luke Concept)", "Stormtrooper (Concept)", "Jaxxon", "The Ronin"],
            [new("Mara Jade Skywalker", 110)],
            "85 fragmentos de Darth Jar Jar + materiales de Era y reliquias"),
        new(
            4,
            7,
            ["Mara Jade Skywalker", "Yoda (Dark Side Vision)", "Starkiller (Luke Concept)", "Stormtrooper (Concept)", "Jaxxon", "The Ronin"],
            [
                new("Mara Jade Skywalker", 125),
                new("Yoda (Dark Side Vision)", 125),
                new("Starkiller (Luke Concept)", 125),
                new("Stormtrooper (Concept)", 125),
                new("Jaxxon", 125),
                new("The Ronin", 125)
            ],
            "100 fragmentos de Darth Jar Jar + retrato y título")
    ];

    public static IReadOnlyCollection<ColiseumBossDefinition> ColiseumBosses { get; } =
    [
        new("KRAYTDRAGON", "Krayt Dragon", "Rotación de jefe de Coliseo de Myths & Legends.", "Prioriza tu Era Level más alto y una composición que pueda sostener daño prolongado; conserva el mejor intento diario como referencia."),
        new("ZEFFOTOMBGUARDIAN", "Zeffo Tomb Guardians", "Rotación de jefe de Coliseo de Myths & Legends.", "Prueba composiciones con buen control y daño repartido; compara siempre el porcentaje final con tu mejor marca."),
        new("JOTAZ", "Jotaz", "Rotación de jefe de Coliseo de Myths & Legends.", "Favorece pruebas de daño sostenido y control; cambia líder y Loaned Units antes de gastar recursos en una sola composición."),
        new("DRYAX", "Dryax", "Rotación de jefe de Coliseo de Myths & Legends.", "Usa el boss como banco de pruebas para líderes de Era y Loaned Units, buscando primero consistencia y después puntuación.")
    ];

    public static IReadOnlyCollection<ColiseumTierGuidance> ColiseumTierGuidance { get; } =
    [
        new(6, 60, "CG ajustó Tier 6 para mejorar puntuación a EL 60+."),
        new(7, 70, "CG ajustó Tier 7 para mejorar puntuación a EL 70+."),
        new(8, 80, "CG ajustó Tier 8 para mejorar puntuación a EL 80+."),
        new(9, 90, "CG ajustó Tier 9 para mejorar puntuación a EL 90+."),
        new(10, null, "Tier alto: usa la mejor combinación disponible; el roster público no expone Era Level."),
        new(11, null, "Tier alto: compara resultado real y evita asumir que estrellas o reliquias Legacy equivalen a Era Level."),
        new(12, null, "Tier máximo actual; úsalo como objetivo de largo plazo de la Era, no como requisito automático del planner.")
    ];
}

internal sealed record EraUnitDefinition(
    string Key,
    string Name,
    string Alignment,
    string Role,
    IReadOnlyCollection<string> Categories,
    bool IsJourneyUnit,
    IReadOnlyCollection<string> Aliases,
    string PrimarySynergy,
    string Notes);

internal sealed record EraJourneyTierDefinition(
    int Tier,
    int RequiredStars,
    IReadOnlyCollection<string> RequiredUnits,
    IReadOnlyCollection<EraLevelRequirement> EraLevelRequirements,
    string RewardSummary);

internal sealed record ColiseumBossDefinition(
    string Id,
    string Name,
    string RotationNote,
    string StrategyNote);
