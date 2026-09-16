namespace Swgoh.Application.TerritoryBattles;

internal static class RiseOfEmpireMissionGuideCatalog
{
    public const string Version = "2026-09-16.2";

    public static IReadOnlyDictionary<string, IReadOnlyCollection<RiseOfEmpireMissionGuideDefinition>> ByPlanet { get; } =
        new Dictionary<string, IReadOnlyCollection<RiseOfEmpireMissionGuideDefinition>>(StringComparer.OrdinalIgnoreCase)
        {
            ["mustafar"] =
            [
                Combat("mustafar-dark", "Combates Dark Side", "Dark Side R5+", 5,
                    Team("Lord Vader", "Alta", "Núcleo resistente para las oleadas de Mustafar.",
                        C("Lord Vader", 5, "LORDVADER"), C("Darth Vader", 5, "DARTHVADER"), C("Royal Guard", 5, "ROYALGUARD"), C("Maul", 5, "MAULS7", "Maul"), C("Grand Moff Tarkin", 5, "GRANDMOFFTARKIN")),
                    Team("SLKR First Order", "Alta", "Alternativa Dark Side muy estable si no quieres gastar Lord Vader.",
                        C("Supreme Leader Kylo Ren", 5, "SUPREMELEADERKYLOREN"), C("Kylo Ren (Unmasked)", 5, "KYLORENUNMASKED"), C("General Hux", 5, "GENERALHUX"), C("Sith Trooper", 5, "FOSITHTROOPER"), C("First Order Stormtrooper", 5, "FIRSTORDERSTORMTROOPER"))),
                Combat("mustafar-lv", "Lord Vader", "Lord Vader R5+", 5,
                    Team("Lord Vader · misión", "Alta", "Equipo centrado en la unidad requerida.",
                        C("Lord Vader", 5, "LORDVADER"), C("Darth Vader", 5, "DARTHVADER"), C("Royal Guard", 5, "ROYALGUARD"), C("Maul", 5, "MAULS7", "Maul"), C("Grand Moff Tarkin", 5, "GRANDMOFFTARKIN")),
                    C("Lord Vader", 5, "LORDVADER")),
                Fleet("mustafar-fleet", "Flota Dark Side", "Naves a 7★", 
                    FleetTeam("Imperio con Scythe", "Alta", "Flota imperial de referencia para Mustafar.",
                        S("Executrix", "CAPITALTARKIN", "Executrix"), S("Scythe", "SCYTHE"), S("TIE Advanced x1", "TIEADVANCED"), S("Imperial TIE Fighter", "TIEFIGHTERIMPERIAL"), S("TIE Bomber", "TIEBOMBER")))
            ],
            ["corellia"] =
            [
                Combat("corellia-jabba", "Jabba", "Jabba the Hutt R5+", 5,
                    Team("Jabba Hutt Cartel", "Alta", "Equipo natural para el nodo que exige Jabba.",
                        C("Jabba the Hutt", 5, "JABBATHEHUTT"), C("Krrsantan", 5, "KRRSANTAN"), C("Boushh (Leia Organa)", 5, "BOUSHH"), C("Skiff Guard (Lando Calrissian)", 5, "SKIFFGUARDLANDO"), C("Embo", 5, "EMBO")),
                    C("Jabba the Hutt", 5, "JABBATHEHUTT")),
                Combat("corellia-aphra", "Doctor Aphra", "Doctor Aphra R5+", 5,
                    Team("Aphra droides", "Alta", "Aphra con sus droides y dos anclas Dark Side.",
                        C("Doctor Aphra", 5, "DOCTORAPHRA"), C("BT-1", 5, "BT1"), C("0-0-0", 5, "TRIPLEZERO"), C("Darth Vader", 5, "DARTHVADER"), C("IG-88", 5, "IG88")),
                    C("Doctor Aphra", 5, "DOCTORAPHRA")),
                Special("corellia-qira", "Qi'ra + Young Han", "Qi'ra y Young Han Solo R5+", 5,
                    Team("Qi'ra · Rey · Vandor", "Alta", "Composición de referencia centrada en aguante y recuperación.",
                        C("Qi'ra", 5, "QIRA"), C("Rey", 5, "GLREY", "Rey"), C("Vandor Chewbacca", 5, "VANDORCHEWBACCA"), C("L3-37", 5, "L3_37", "L3-37"), C("Young Han Solo", 5, "YOUNGHAN")),
                    C("Qi'ra", 5, "QIRA"), C("Young Han Solo", 5, "YOUNGHAN")),
                Fleet("corellia-fleet", "Flota · Lando's Millennium Falcon", "Lando's Millennium Falcon a 7★", 
                    FleetTeam("Profundity con Lando", "Media", "Mantiene la nave requerida dentro de una flota Rebel funcional.",
                        S("Profundity", "PROFUNDITY"), S("Han's Millennium Falcon", "MILLENNIUMFALCON"), S("Outrider", "OUTRIDER"), S("Rebel Y-wing", "YWINGREBEL"), S("Lando's Millennium Falcon", "MILLENNIUMFALCONPRISTINE", "Lando's Millennium Falcon")),
                    S("Lando's Millennium Falcon", "MILLENNIUMFALCONPRISTINE", "Lando's Millennium Falcon"))
            ],
            ["coruscant"] =
            [
                Combat("coruscant-jedi", "Jedi", "Jedi R5+", 5,
                    Team("Jedi Master Luke", "Alta", "JML con Jedi de apoyo para el nodo Jedi.",
                        C("Jedi Master Luke Skywalker", 5, "JEDIMASTERLUKE"), C("Jedi Knight Luke Skywalker", 5, "JEDIKNIGHTLUKE"), C("Hermit Yoda", 5, "HERMITYODA"), C("Grand Master Yoda", 5, "GRANDMASTERYODA"), C("Jolee Bindo", 5, "JOLEE"))),
                Combat("coruscant-mace-kit", "Mace Windu + Kit Fisto", "Jedi R5+; Mace Windu y Kit Fisto obligatorios", 5,
                    Team("JML · Mace · Kit", "Alta", "JML aporta margen al nodo con dos plazas fijadas.",
                        C("Jedi Master Luke Skywalker", 5, "JEDIMASTERLUKE"), C("Mace Windu", 5, "MACEWINDU"), C("Kit Fisto", 5, "KITFISTO"), C("Jedi Knight Luke Skywalker", 5, "JEDIKNIGHTLUKE"), C("Hermit Yoda", 5, "HERMITYODA")),
                    C("Mace Windu", 5, "MACEWINDU"), C("Kit Fisto", 5, "KITFISTO")),
                Fleet("coruscant-fleet", "Flota · Outrider", "Outrider a 7★", 
                    FleetTeam("Profundity", "Alta", "La flota Rebel estándar ya incorpora Outrider.",
                        S("Profundity", "PROFUNDITY"), S("Rebel Y-wing", "YWINGREBEL"), S("Han's Millennium Falcon", "MILLENNIUMFALCON"), S("Outrider", "OUTRIDER"), S("Phantom II", "PHANTOM2")),
                    S("Outrider", "OUTRIDER"))
            ],
            ["geonosis"] =
            [
                Combat("geonosis-dark", "Combates Dark Side", "Dark Side R6+", 6,
                    Team("SLKR", "Alta", "Equipo seguro para uno de los nodos genéricos.",
                        C("Supreme Leader Kylo Ren", 6, "SUPREMELEADERKYLOREN"), C("Kylo Ren (Unmasked)", 6, "KYLORENUNMASKED"), C("General Hux", 6, "GENERALHUX"), C("Sith Trooper", 6, "FOSITHTROOPER"), C("First Order Stormtrooper", 6, "FIRSTORDERSTORMTROOPER")),
                    Team("Doctor Aphra", "Media", "Alternativa probada para reservar una GL.",
                        C("Doctor Aphra", 6, "DOCTORAPHRA"), C("BT-1", 6, "BT1"), C("0-0-0", 6, "TRIPLEZERO"), C("Darth Vader", 6, "DARTHVADER"), C("HK-47", 6, "HK47"))),
                Combat("geonosis-geos", "Geonosianos", "5 Geonosianos R6+", 6,
                    Team("Geonosianos", "Alta", "Composición completa de Geonosianos.",
                        C("Geonosian Brood Alpha", 6, "GEONOSIANBROODALPHA"), C("Geonosian Spy", 6, "GEONOSIANSPY"), C("Geonosian Soldier", 6, "GEONOSIANSOLDIER"), C("Sun Fac", 6, "SUNFAC"), C("Poggle the Lesser", 6, "POGGLETHELESSER"))),
                Fleet("geonosis-fleet", "Flota Dark Side", "Naves a 7★", 
                    FleetTeam("Executor", "Alta", "Flota Dark Side consistente para la fase 2.",
                        S("Executor", "CAPITALEXECUTOR", "Executor"), S("Hound's Tooth", "HOUNDSTOOTH"), S("Razor Crest", "RAZORCREST"), S("Xanadu Blood", "XANADUBLOOD"), S("IG-2000", "IG2000")))
            ],
            ["felucia"] =
            [
                Combat("felucia-young-lando", "Young Lando", "Young Lando Calrissian R6+", 6,
                    Team("Young Lando + Scoundrels", "Media", "Prioriza control y supervivencia alrededor de Young Lando.",
                        C("Young Lando Calrissian", 6, "YOUNGLANDO"), C("Dash Rendar", 6, "DASHRENDAR"), C("Vandor Chewbacca", 6, "VANDORCHEWBACCA"), C("L3-37", 6, "L3_37"), C("Hondo Ohnaka", 6, "HONDO")),
                    C("Young Lando Calrissian", 6, "YOUNGLANDO")),
                Combat("felucia-jabba", "Jabba", "Jabba the Hutt R6+", 6,
                    Team("Jabba Hutt Cartel", "Alta", "Equipo estándar de Jabba.",
                        C("Jabba the Hutt", 6, "JABBATHEHUTT"), C("Krrsantan", 6, "KRRSANTAN"), C("Boushh (Leia Organa)", 6, "BOUSHH"), C("Skiff Guard (Lando Calrissian)", 6, "SKIFFGUARDLANDO"), C("Embo", 6, "EMBO")),
                    C("Jabba the Hutt", 6, "JABBATHEHUTT")),
                Special("felucia-hondo", "Hondo", "Hondo Ohnaka R6+", 6,
                    Team("Hondo + mercenarios", "Media", "Equipo de control para el nodo especial de Hondo.",
                        C("Hondo Ohnaka", 6, "HONDO"), C("Baylan Skoll", 6, "BAYLANSKOLL"), C("Shin Hati", 6, "SHINHATI"), C("Marrok", 6, "MARROK"), C("L3-37", 6, "L3_37")),
                    C("Hondo Ohnaka", 6, "HONDO")),
                Fleet("felucia-fleet", "Flota mixta", "Naves a 7★",
                    FleetTeam("Executor", "Alta", "Opción consistente para Felucia.",
                        S("Executor", "CAPITALEXECUTOR", "Executor"), S("Hound's Tooth", "HOUNDSTOOTH"), S("Razor Crest", "RAZORCREST"), S("Xanadu Blood", "XANADUBLOOD"), S("IG-2000", "IG2000")))
            ],
            ["bracca"] =
            [
                Combat("bracca-jedi", "Jedi", "Jedi R6+", 6,
                    Team("Jedi Master Mace Windu", "Alta", "Si está disponible, su equipo Jedi reduce el coste de reservar otras GL.",
                        C("Jedi Master Mace Windu", 6, "Jedi Master Mace Windu"), C("Mace Windu", 6, "MACEWINDU"), C("Grand Master Yoda", 6, "GRANDMASTERYODA"), C("Shaak Ti", 6, "SHAAKTI"), C("Plo Koon", 6, "PLOKOON")),
                    Team("Jedi Master Luke", "Alta", "Alternativa Jedi muy segura.",
                        C("Jedi Master Luke Skywalker", 6, "JEDIMASTERLUKE"), C("Jedi Knight Luke Skywalker", 6, "JEDIKNIGHTLUKE"), C("Hermit Yoda", 6, "HERMITYODA"), C("Grand Master Yoda", 6, "GRANDMASTERYODA"), C("Jolee Bindo", 6, "JOLEE"))),
                Special("bracca-zeffo", "Desbloqueo de Zeffo", "Cere Junda + Cal Kestis o Jedi Knight Cal Kestis R7+", 7,
                    Team("Cere + Cal", "Alta", "La misión de desbloqueo solo exige el dúo requerido.",
                        C("Cere Junda", 7, "CEREJUNDA"), C("Cal Kestis", 7, "CALKESTIS", "Jedi Knight Cal Kestis", "JEDIKNIGHTCAL")),
                    C("Cere Junda", 7, "CEREJUNDA"), C("Cal Kestis", 7, "CALKESTIS", "JEDIKNIGHTCAL")),
                Fleet("bracca-fleet", "Flota Light Side", "Naves a 7★",
                    FleetTeam("Negotiator", "Alta", "Flota República sólida para Bracca.",
                        S("Negotiator", "CAPITALNEGOTIATOR", "Negotiator"), S("Anakin's Eta-2 Starfighter", "JEDISTARFIGHTERANAKIN"), S("Marauder", "BADBATCHMARAUDER"), S("BTL-B Y-wing Starfighter", "YWINGCLONEWARS"), S("Ahsoka Tano's Jedi Starfighter", "JEDISTARFIGHTERAHSOKATANO")))
            ],
            ["dathomir"] =
            [
                Combat("dathomir-empire", "Imperio", "5 unidades del Imperio R7+", 7,
                    Team("Lord Vader Imperio", "Alta", "Lord Vader lidera el nodo de Imperio con mucha estabilidad.",
                        C("Lord Vader", 7, "LORDVADER"), C("Darth Vader", 7, "DARTHVADER"), C("Royal Guard", 7, "ROYALGUARD"), C("Grand Moff Tarkin", 7, "GRANDMOFFTARKIN"), C("Admiral Piett", 7, "ADMIRALPIETT"))),
                Combat("dathomir-aphra", "Doctor Aphra", "Doctor Aphra R7+", 7,
                    Team("Aphra · BT-1 · GG · HK · Vader", "Alta", "Composición observada capaz de completar la misión.",
                        C("Doctor Aphra", 7, "DOCTORAPHRA"), C("BT-1", 7, "BT1"), C("General Grievous", 7, "GRIEVOUS"), C("HK-47", 7, "HK47"), C("Darth Vader", 7, "DARTHVADER")),
                    C("Doctor Aphra", 7, "DOCTORAPHRA")),
                Special("dathomir-merrin", "Merrin", "Hermanas de la Noche + Merrin R7+", 7,
                    Team("Nightsisters", "Alta", "Equipo completo de Hermanas de la Noche con Merrin.",
                        C("Mother Talzin", 7, "MOTHERTALZIN"), C("Old Daka", 7, "DAKA"), C("Nightsister Zombie", 7, "NIGHTSISTERZOMBIE"), C("Asajj Ventress", 7, "ASAJVENTRESS"), C("Merrin", 7, "MERRIN")),
                    C("Merrin", 7, "MERRIN"))
            ],
            ["tatooine"] =
            [
                Combat("tatooine-jabba", "Jabba", "Jabba the Hutt R7+", 7,
                    Team("Jabba Hutt Cartel", "Alta", "Equipo estándar de Jabba.",
                        C("Jabba the Hutt", 7, "JABBATHEHUTT"), C("Krrsantan", 7, "KRRSANTAN"), C("Boushh (Leia Organa)", 7, "BOUSHH"), C("Skiff Guard (Lando Calrissian)", 7, "SKIFFGUARDLANDO"), C("Embo", 7, "EMBO")),
                    C("Jabba the Hutt", 7, "JABBATHEHUTT")),
                Combat("tatooine-fennec", "Fennec Shand", "Fennec Shand R7+", 7,
                    Team("Fennec Bounty Hunters", "Alta", "Bounty Hunters alrededor de Fennec para el nodo dedicado.",
                        C("Fennec Shand", 7, "FENNECSHAND"), C("Bossk", 7, "BOSSK"), C("The Mandalorian", 7, "THEMANDALORIAN"), C("Greef Karga", 7, "GREEFKARGA"), C("Boba Fett", 7, "BOBAFETT")),
                    C("Fennec Shand", 7, "FENNECSHAND")),
                Special("tatooine-reva", "Third Sister / Reva", "Inquisitorius R7+ con Grand Inquisitor", 7,
                    Team("Grand Inquisitor", "Alta", "Equipo de Inquisitorius para obtener shards de Reva.",
                        C("Grand Inquisitor", 7, "GRANDINQUISITOR"), C("Fifth Brother", 7, "FIFTHBROTHER"), C("Seventh Sister", 7, "SEVENTHSISTER"), C("Eighth Brother", 7, "EIGHTHBROTHER"), C("Ninth Sister", 7, "NINTHSISTER")),
                    C("Grand Inquisitor", 7, "GRANDINQUISITOR")),
                Special("tatooine-mandalore", "Desbloqueo de Mandalore", "Bo-Katan (Mand'alor) + BAM + otro Mandaloriano R7+", 7,
                    Team("Bo-Katan · BAM · IG-12 & Grogu", "Alta", "Trío de referencia para el desbloqueo de Mandalore.",
                        C("Bo-Katan (Mand'alor)", 7, "BOKATANMANDALORE"), C("The Mandalorian (Beskar Armor)", 7, "THEMANDALORIANBESKARARMOR"), C("IG-12 & Grogu", 7, "IG12")),
                    C("Bo-Katan (Mand'alor)", 7, "BOKATANMANDALORE"), C("The Mandalorian (Beskar Armor)", 7, "THEMANDALORIANBESKARARMOR")),
                Fleet("tatooine-fleet", "Executor", "Executor y naves a 7★",
                    FleetTeam("Executor", "Alta", "Flota exigida/recomendada para Tatooine.",
                        S("Executor", "CAPITALEXECUTOR", "Executor"), S("Hound's Tooth", "HOUNDSTOOTH"), S("Razor Crest", "RAZORCREST"), S("Xanadu Blood", "XANADUBLOOD"), S("IG-2000", "IG2000")),
                    S("Executor", "CAPITALEXECUTOR", "Executor"))
            ],
            ["kashyyyk"] =
            [
                Combat("kashyyyk-wookiee", "Wookiees", "5 Wookiees Light Side R7+", 7,
                    Team("Tarrful Wookiees", "Alta", "Composición de referencia del nodo Wookiee.",
                        C("Tarrful", 7, "TARRFUL"), C("Chewbacca", 7, "CHEWBACCALEGENDARY"), C("Vandor Chewbacca", 7, "VANDORCHEWBACCA"), C("Threepio & Chewie", 7, "CHEWBACCAONC3PO"), C("Zaalbar", 7, "ZAALBAR"))),
                Special("kashyyyk-saw", "Saw Gerrera", "Saw Gerrera + Rebel Fighters R7+", 7,
                    Team("Saw · Luthen · Rogue One", "Alta", "Composición moderna de referencia para el especial de Saw.",
                        C("Saw Gerrera", 7, "SAWGERRERA"), C("Luthen Rael", 7, "LUTHENRAEL"), C("Cassian Andor", 7, "CASSIANANDOR"), C("K-2SO", 7, "K2SO"), C("Jyn Erso", 7, "JYNERSO")),
                    C("Saw Gerrera", 7, "SAWGERRERA")),
                Fleet("kashyyyk-fleet", "Profundity", "Profundity y naves a 7★",
                    FleetTeam("Profundity", "Alta", "Flota Rebel estándar.",
                        S("Profundity", "PROFUNDITY"), S("Rebel Y-wing", "YWINGREBEL"), S("Han's Millennium Falcon", "MILLENNIUMFALCON"), S("Outrider", "OUTRIDER"), S("Phantom II", "PHANTOM2")),
                    S("Profundity", "PROFUNDITY"))
            ],
            ["zeffo"] =
            [
                Combat("zeffo-ufu", "Unaligned Force Users", "UFU R7+", 7,
                    Team("Cere UFU", "Alta", "Cere articula el equipo UFU de Zeffo.",
                        C("Cere Junda", 7, "CEREJUNDA"), C("Cal Kestis", 7, "CALKESTIS"), C("Rey", 7, "GLREY", "Rey"), C("Ben Solo", 7, "BENSOLO"), C("Maul", 7, "MAULS7", "Maul"))),
                Combat("zeffo-jkck", "Jedi Knight Cal Kestis", "Jedi Knight Cal Kestis R7+", 7,
                    Team("JKCK Jedi", "Alta", "Equipo Jedi alrededor de JKCK.",
                        C("Jedi Knight Cal Kestis", 7, "JEDIKNIGHTCAL"), C("Jedi Master Luke Skywalker", 7, "JEDIMASTERLUKE"), C("Jedi Knight Luke Skywalker", 7, "JEDIKNIGHTLUKE"), C("Hermit Yoda", 7, "HERMITYODA"), C("Grand Master Yoda", 7, "GRANDMASTERYODA")),
                    C("Jedi Knight Cal Kestis", 7, "JEDIKNIGHTCAL")),
                Special("zeffo-clones", "Clone Troopers", "Clone Troopers R7+", 7,
                    Team("501st Clones", "Alta", "Quinteto de clones de referencia.",
                        C("Captain Rex", 7, "CAPTAINREX"), C("Rex", 7, "CT7567"), C("Fives", 7, "CT5555"), C("Echo", 7, "CT210408"), C("ARC Trooper", 7, "ARCTROOPER501ST"))),
                Fleet("zeffo-fleet", "Negotiator", "Negotiator y naves a 7★",
                    FleetTeam("Negotiator", "Alta", "Flota República para Zeffo.",
                        S("Negotiator", "CAPITALNEGOTIATOR", "Negotiator"), S("Anakin's Eta-2 Starfighter", "JEDISTARFIGHTERANAKIN"), S("Marauder", "BADBATCHMARAUDER"), S("BTL-B Y-wing Starfighter", "YWINGCLONEWARS"), S("Ahsoka Tano's Jedi Starfighter", "JEDISTARFIGHTERAHSOKATANO")),
                    S("Negotiator", "CAPITALNEGOTIATOR", "Negotiator"))
            ],
            ["medical-station"] =
            [
                Combat("medical-dark", "Combates Dark Side", "Dark Side R8+", 8,
                    Team("Lord Vader", "Alta", "Primera opción para los nodos genéricos de Medical Station.",
                        C("Lord Vader", 8, "LORDVADER"), C("Darth Vader", 8, "DARTHVADER"), C("Royal Guard", 8, "ROYALGUARD"), C("Maul", 8, "MAULS7", "Maul"), C("Grand Moff Tarkin", 8, "GRANDMOFFTARKIN")),
                    Team("SLKR", "Alta", "Segunda opción Dark Side de alto margen.",
                        C("Supreme Leader Kylo Ren", 8, "SUPREMELEADERKYLOREN"), C("Kylo Ren (Unmasked)", 8, "KYLORENUNMASKED"), C("General Hux", 8, "GENERALHUX"), C("Sith Trooper", 8, "FOSITHTROOPER"), C("First Order Stormtrooper", 8, "FIRSTORDERSTORMTROOPER"))),
                Special("medical-reva", "Third Sister", "Inquisitorius + Third Sister R8+", 8,
                    Team("Reva Inquisitorius", "Alta", "Equipo completo de Inquisitorius con Reva.",
                        C("Third Sister", 8, "THIRDSISTER"), C("Grand Inquisitor", 8, "GRANDINQUISITOR"), C("Fifth Brother", 8, "FIFTHBROTHER"), C("Seventh Sister", 8, "SEVENTHSISTER"), C("Eighth Brother", 8, "EIGHTHBROTHER")),
                    C("Third Sister", 8, "THIRDSISTER"))
            ],
            ["kessel"] =
            [
                Combat("kessel-jabba", "Jabba", "Jabba the Hutt R8+", 8,
                    Team("Jabba Hutt Cartel", "Alta", "Equipo estándar de Jabba.",
                        C("Jabba the Hutt", 8, "JABBATHEHUTT"), C("Krrsantan", 8, "KRRSANTAN"), C("Boushh (Leia Organa)", 8, "BOUSHH"), C("Skiff Guard (Lando Calrissian)", 8, "SKIFFGUARDLANDO"), C("Embo", 8, "EMBO")),
                    C("Jabba the Hutt", 8, "JABBATHEHUTT")),
                Special("kessel-qira", "Qi'ra + L3-37", "Qi'ra y L3-37 R8+", 8,
                    Team("Baylan · Shin · Marrok · Qi'ra · L3", "Alta", "Composición moderna de referencia para el especial de Kessel.",
                        C("Baylan Skoll", 8, "BAYLANSKOLL"), C("Shin Hati", 8, "SHINHATI"), C("Marrok", 8, "MARROK"), C("Qi'ra", 8, "QIRA"), C("L3-37", 8, "L3_37")),
                    C("Qi'ra", 8, "QIRA"), C("L3-37", 8, "L3_37")),
                Fleet("kessel-fleet", "Flota · Ghost", "Ghost a 7★",
                    FleetTeam("Profundity con Ghost", "Alta", "Flota Rebel con la nave requerida.",
                        S("Profundity", "PROFUNDITY"), S("Rebel Y-wing", "YWINGREBEL"), S("Han's Millennium Falcon", "MILLENNIUMFALCON"), S("Ghost", "GHOST"), S("Outrider", "OUTRIDER")),
                    S("Ghost", "GHOST"))
            ],
            ["lothal"] =
            [
                Combat("lothal-jedi", "Jedi", "Jedi R8+", 8,
                    Team("Jedi Master Luke", "Alta", "Jedi de alto margen para Lothal.",
                        C("Jedi Master Luke Skywalker", 8, "JEDIMASTERLUKE"), C("Jedi Knight Luke Skywalker", 8, "JEDIKNIGHTLUKE"), C("Hermit Yoda", 8, "HERMITYODA"), C("Grand Master Yoda", 8, "GRANDMASTERYODA"), C("Jolee Bindo", 8, "JOLEE"))),
                Combat("lothal-phoenix", "Phoenix", "Phoenix R8+", 8,
                    Team("Hera + Captain Rex", "Alta", "Phoenix moderno con Captain Rex.",
                        C("Hera Syndulla", 8, "HERASYNDULLAS3"), C("Captain Rex", 8, "CAPTAINREX"), C("Kanan Jarrus", 8, "KANANJARRUSS3"), C("Sabine Wren", 8, "SABINEWRENS3"), C("Chopper", 8, "CHOPPERS3"))),
                Fleet("lothal-fleet", "Flota Light Side", "Naves a 7★",
                    FleetTeam("Profundity", "Alta", "Opción Rebel de referencia.",
                        S("Profundity", "PROFUNDITY"), S("Rebel Y-wing", "YWINGREBEL"), S("Han's Millennium Falcon", "MILLENNIUMFALCON"), S("Outrider", "OUTRIDER"), S("Phantom II", "PHANTOM2")))
            ],
            ["mandalore"] =
            [
                Combat("mandalore-bo", "Bo-Katan (Mand'alor)", "Bo-Katan (Mand'alor) R9", 9,
                    Team("Bo-Katan Mandalorianos", "Alta", "Equipo centrado en Bo-Katan (Mand'alor).",
                        C("Bo-Katan (Mand'alor)", 9, "BOKATANMANDALORE"), C("The Mandalorian (Beskar Armor)", 8, "THEMANDALORIANBESKARARMOR"), C("IG-12 & Grogu", 8, "IG12"), C("Paz Vizsla", 8, "PAZVIZSLA"), C("The Armorer", 8, "ARMORER")),
                    C("Bo-Katan (Mand'alor)", 9, "BOKATANMANDALORE")),
                Combat("mandalore-dtmg", "Dark Trooper Moff Gideon", "Dark Trooper Moff Gideon R8+", 8,
                    Team("Imperial Remnant", "Alta", "DTMG con Remanente Imperial.",
                        C("Dark Trooper Moff Gideon", 8, "MOFFGIDEONS1"), C("Scout Trooper", 8, "SCOUTTROOPER_V3"), C("Death Trooper (Peridea)", 8, "DEATHTROOPERPERIDEA"), C("Enoch", 8, "CAPTAINENOCH"), C("Night Trooper", 8, "NIGHTTROOPER")),
                    C("Dark Trooper Moff Gideon", 8, "MOFFGIDEONS1", "Dark Trooper Moff Gideon")),
                Fleet("mandalore-fleet", "Flota · Gauntlet Starfighter", "Gauntlet Starfighter a 7★",
                    FleetTeam("Imperio con Gauntlet", "Media", "Integra la nave requerida en una flota imperial.",
                        S("Executrix", "CAPITALTARKIN", "Executrix"), S("Gauntlet Starfighter", "GAUNTLETSTARFIGHTER"), S("Scythe", "SCYTHE"), S("TIE Advanced x1", "TIEADVANCED"), S("Imperial TIE Fighter", "TIEFIGHTERIMPERIAL")),
                    S("Gauntlet Starfighter", "GAUNTLETSTARFIGHTER"))
            ],
            ["malachor"] =
            [
                Combat("malachor-dark", "Combates Dark Side", "Dark Side R9+", 9,
                    Team("Sith Eternal Emperor", "Alta", "Sith R9 alrededor de SEE para Malachor.",
                        C("Sith Eternal Emperor", 9, "SITHPALPATINE"), C("Darth Malgus", 9, "DARTHMALGUS"), C("Darth Malak", 9, "DARTHMALAK"), C("Sith Empire Trooper", 9, "SITHTROOPER"), C("Darth Revan", 9, "DARTHREVAN")),
                    Team("SLKR", "Alta", "Alternativa Dark Side muy consistente.",
                        C("Supreme Leader Kylo Ren", 9, "SUPREMELEADERKYLOREN"), C("Kylo Ren (Unmasked)", 9, "KYLORENUNMASKED"), C("General Hux", 9, "GENERALHUX"), C("Sith Trooper", 9, "FOSITHTROOPER"), C("First Order Stormtrooper", 9, "FIRSTORDERSTORMTROOPER"))),
                Combat("malachor-inqs", "Trío Inquisitorius", "Eighth Brother + Fifth Brother + Seventh Sister R9+", 9,
                    Team("Inquisitorius", "Alta", "Completa el trío requerido con Grand Inquisitor y Reva.",
                        C("Eighth Brother", 9, "EIGHTHBROTHER"), C("Fifth Brother", 9, "FIFTHBROTHER"), C("Seventh Sister", 9, "SEVENTHSISTER"), C("Grand Inquisitor", 9, "GRANDINQUISITOR"), C("Third Sister", 9, "THIRDSISTER")),
                    C("Eighth Brother", 9, "EIGHTHBROTHER"), C("Fifth Brother", 9, "FIFTHBROTHER"), C("Seventh Sister", 9, "SEVENTHSISTER"))
            ],
            ["vandor"] =
            [
                Combat("vandor-jabba", "Jabba", "Jabba the Hutt R9+", 9,
                    Team("Jabba Hutt Cartel", "Alta", "Equipo estándar de Jabba.",
                        C("Jabba the Hutt", 9, "JABBATHEHUTT"), C("Krrsantan", 9, "KRRSANTAN"), C("Boushh (Leia Organa)", 9, "BOUSHH"), C("Skiff Guard (Lando Calrissian)", 9, "SKIFFGUARDLANDO"), C("Embo", 9, "EMBO")),
                    C("Jabba the Hutt", 9, "JABBATHEHUTT")),
                Special("vandor-young-han", "Young Han + Vandor Chewbacca", "Young Han Solo y Vandor Chewbacca R9+", 9,
                    Team("Qi'ra Scoundrels", "Media", "Equipo de supervivencia alrededor de las dos unidades obligatorias.",
                        C("Qi'ra", 9, "QIRA"), C("Young Han Solo", 9, "YOUNGHAN"), C("Vandor Chewbacca", 9, "VANDORCHEWBACCA"), C("L3-37", 9, "L3_37"), C("Dash Rendar", 9, "DASHRENDAR")),
                    C("Young Han Solo", 9, "YOUNGHAN"), C("Vandor Chewbacca", 9, "VANDORCHEWBACCA")),
                Fleet("vandor-fleet", "Flota mixta", "Naves a 7★",
                    FleetTeam("Leviathan", "Alta", "Flota endgame de referencia para la fase 5.",
                        S("Leviathan", "CAPITALLEVIATHAN", "Leviathan"), S("Fury-class Interceptor", "FURYCLASSINTERCEPTOR"), S("Mark VI Interceptor", "MARK6INTERCEPTOR"), S("Sith Fighter", "SITHFIGHTER"), S("TIE Dagger", "TIEDAGGER")))
            ],
            ["ring-of-kafrene"] =
            [
                Combat("kafrene-light", "Combates Light Side", "Light Side R9+", 9,
                    Team("Leia Organa", "Alta", "GL Leia es una de las mejores anclas Light Side en R9.",
                        C("Leia Organa", 9, "GLLEIA", "Leia Organa"), C("Drogan", 9, "DROGAN"), C("R2-D2", 9, "R2D2_LEGENDARY"), C("Captain Rex", 9, "CAPTAINREX"), C("Old Ben", 9, "OLDBENKENOBI"))),
                Combat("kafrene-cassian", "Cassian + K-2SO", "Cassian Andor + K-2SO R9+", 9,
                    Team("Admiral Raddus Rogue One", "Alta", "Rogue One alrededor del dúo obligatorio.",
                        C("Admiral Raddus", 9, "ADMIRALRADDUS"), C("Jyn Erso", 9, "JYNERSO"), C("Cassian Andor", 9, "CASSIANANDOR"), C("K-2SO", 9, "K2SO"), C("Baze Malbus", 9, "BAZEMALBUS")),
                    C("Cassian Andor", 9, "CASSIANANDOR"), C("K-2SO", 9, "K2SO")),
                Fleet("kafrene-fleet", "Flota Light Side", "Naves a 7★",
                    FleetTeam("Profundity", "Alta", "Flota Rebel endgame.",
                        S("Profundity", "PROFUNDITY"), S("Rebel Y-wing", "YWINGREBEL"), S("Han's Millennium Falcon", "MILLENNIUMFALCON"), S("Outrider", "OUTRIDER"), S("Phantom II", "PHANTOM2")))
            ],
            ["death-star"] =
            [
                Combat("death-star-iden", "Iden Versio", "Iden Versio R9+", 9,
                    Team("Iden + Sith", "Alta", "Composición de referencia con tanques Sith de alto relic.",
                        C("Iden Versio", 9, "IDENVERSIO"), C("Supreme Leader Kylo Ren", 9, "SUPREMELEADERKYLOREN"), C("Darth Malgus", 9, "DARTHMALGUS"), C("Darth Malak", 9, "DARTHMALAK"), C("Sith Empire Trooper", 9, "SITHTROOPER")),
                    C("Iden Versio", 9, "IDENVERSIO")),
                Combat("death-star-vader", "Darth Vader", "Darth Vader R9+", 9,
                    Team("Darth Vader solo", "Alta", "El nodo dedicado puede plantearse con Vader como única unidad obligatoria.",
                        C("Darth Vader", 9, "DARTHVADER")),
                    C("Darth Vader", 9, "DARTHVADER")),
                Fleet("death-star-fleet", "Flota Imperial TIE Fighter", "Imperial TIE Fighter a 7★",
                    FleetTeam("Executrix · Scythe · TIE Advanced", "Alta", "Trío imperial de referencia para la misión de flota.",
                        S("Executrix", "CAPITALTARKIN", "Executrix"), S("Imperial TIE Fighter", "TIEFIGHTERIMPERIAL"), S("Scythe", "SCYTHE"), S("TIE Advanced x1", "TIEADVANCED"), S("TIE Bomber", "TIEBOMBER")),
                    S("Imperial TIE Fighter", "TIEFIGHTERIMPERIAL"))
            ],
            ["hoth"] =
            [
                Combat("hoth-jabba", "Jabba", "Jabba the Hutt R9+", 9,
                    Team("Jabba Hutt Cartel", "Alta", "Equipo estándar de Jabba.",
                        C("Jabba the Hutt", 9, "JABBATHEHUTT"), C("Krrsantan", 9, "KRRSANTAN"), C("Boushh (Leia Organa)", 9, "BOUSHH"), C("Skiff Guard (Lando Calrissian)", 9, "SKIFFGUARDLANDO"), C("Embo", 9, "EMBO")),
                    C("Jabba the Hutt", 9, "JABBATHEHUTT")),
                Special("hoth-aphra", "Aphra + BT-1 + 0-0-0", "Doctor Aphra, BT-1 y 0-0-0 R9+", 9,
                    Team("Aphra droides", "Alta", "Núcleo obligatorio con dos piezas Dark/Light flexibles.",
                        C("Doctor Aphra", 9, "DOCTORAPHRA"), C("BT-1", 9, "BT1"), C("0-0-0", 9, "TRIPLEZERO"), C("Darth Vader", 9, "DARTHVADER"), C("IG-88", 9, "IG88")),
                    C("Doctor Aphra", 9, "DOCTORAPHRA"), C("BT-1", 9, "BT1"), C("0-0-0", 9, "TRIPLEZERO")),
                Fleet("hoth-fleet", "Flota mixta", "Naves a 7★",
                    FleetTeam("Leviathan", "Alta", "Flota endgame para la fase final.",
                        S("Leviathan", "CAPITALLEVIATHAN", "Leviathan"), S("Fury-class Interceptor", "FURYCLASSINTERCEPTOR"), S("Mark VI Interceptor", "MARK6INTERCEPTOR"), S("Sith Fighter", "SITHFIGHTER"), S("TIE Dagger", "TIEDAGGER")))
            ],
            ["scarif"] =
            [
                Combat("scarif-baze", "Baze + Chirrut + SRP", "Baze Malbus, Chirrut Îmwe y Scarif Rebel Pathfinder R9+", 9,
                    Team("Rogue One", "Alta", "Completa el trío obligatorio con Raddus y Jyn.",
                        C("Admiral Raddus", 9, "ADMIRALRADDUS"), C("Jyn Erso", 9, "JYNERSO"), C("Baze Malbus", 9, "BAZEMALBUS"), C("Chirrut Îmwe", 9, "CHIRRUTIMWE"), C("Scarif Rebel Pathfinder", 9, "SCARIFREBEL")),
                    C("Baze Malbus", 9, "BAZEMALBUS"), C("Chirrut Îmwe", 9, "CHIRRUTIMWE"), C("Scarif Rebel Pathfinder", 9, "SCARIFREBEL")),
                Combat("scarif-cassian", "Cassian + Pao + K-2SO", "Cassian Andor, Pao y K-2SO R9+", 9,
                    Team("Rogue One · Cassian", "Alta", "Raddus y Jyn completan el núcleo obligatorio.",
                        C("Admiral Raddus", 9, "ADMIRALRADDUS"), C("Jyn Erso", 9, "JYNERSO"), C("Cassian Andor", 9, "CASSIANANDOR"), C("Pao", 9, "PAO"), C("K-2SO", 9, "K2SO")),
                    C("Cassian Andor", 9, "CASSIANANDOR"), C("Pao", 9, "PAO"), C("K-2SO", 9, "K2SO")),
                Fleet("scarif-fleet", "Profundity", "Profundity y naves a 7★",
                    FleetTeam("Profundity", "Alta", "Flota Rebel exigida para Scarif.",
                        S("Profundity", "PROFUNDITY"), S("Rebel Y-wing", "YWINGREBEL"), S("Han's Millennium Falcon", "MILLENNIUMFALCON"), S("Outrider", "OUTRIDER"), S("Phantom II", "PHANTOM2")),
                    S("Profundity", "PROFUNDITY"))
            ]
        };

    private static RiseOfEmpireMissionGuideDefinition Combat(
        string id, string name, string requirement, int relic,
        RiseOfEmpireConcreteTeamDefinition team,
        params RiseOfEmpireGuideUnitDefinition[] required) =>
        Mission(id, name, "Combat", requirement, relic, false, [team], required);

    private static RiseOfEmpireMissionGuideDefinition Combat(
        string id, string name, string requirement, int relic,
        RiseOfEmpireConcreteTeamDefinition first,
        RiseOfEmpireConcreteTeamDefinition second,
        params RiseOfEmpireGuideUnitDefinition[] required) =>
        Mission(id, name, "Combat", requirement, relic, false, [first, second], required);

    private static RiseOfEmpireMissionGuideDefinition Special(
        string id, string name, string requirement, int relic,
        RiseOfEmpireConcreteTeamDefinition team,
        params RiseOfEmpireGuideUnitDefinition[] required) =>
        Mission(id, name, "Special", requirement, relic, false, [team], required);

    private static RiseOfEmpireMissionGuideDefinition Fleet(
        string id, string name, string requirement,
        RiseOfEmpireConcreteTeamDefinition team,
        params RiseOfEmpireGuideUnitDefinition[] required) =>
        Mission(id, name, "Fleet", requirement, 0, true, [team], required);

    private static RiseOfEmpireMissionGuideDefinition Mission(
        string id, string name, string type, string requirement, int relic, bool isFleet,
        IReadOnlyCollection<RiseOfEmpireConcreteTeamDefinition> teams,
        IReadOnlyCollection<RiseOfEmpireGuideUnitDefinition> required) =>
        new(id, name, type, requirement, relic, isFleet, required, teams);

    private static RiseOfEmpireConcreteTeamDefinition Team(
        string name, string confidence, string rationale,
        params RiseOfEmpireGuideUnitDefinition[] units) => new(name, confidence, rationale, units);

    private static RiseOfEmpireConcreteTeamDefinition FleetTeam(
        string name, string confidence, string rationale,
        params RiseOfEmpireGuideUnitDefinition[] units) => new(name, confidence, rationale, units);

    private static RiseOfEmpireGuideUnitDefinition C(string label, int relic, params string[] aliases) =>
        new(label, false, 7, relic, aliases.Prepend(label).ToArray());

    private static RiseOfEmpireGuideUnitDefinition S(string label, params string[] aliases) =>
        new(label, true, 7, 0, aliases.Prepend(label).ToArray());
}

internal sealed record RiseOfEmpireMissionGuideDefinition(
    string Id,
    string Name,
    string Type,
    string Requirement,
    int MinimumRelicTier,
    bool IsFleet,
    IReadOnlyCollection<RiseOfEmpireGuideUnitDefinition> RequiredUnits,
    IReadOnlyCollection<RiseOfEmpireConcreteTeamDefinition> RecommendedTeams);

internal sealed record RiseOfEmpireConcreteTeamDefinition(
    string Name,
    string Confidence,
    string Rationale,
    IReadOnlyCollection<RiseOfEmpireGuideUnitDefinition> Units);

internal sealed record RiseOfEmpireGuideUnitDefinition(
    string Label,
    bool IsShip,
    int MinimumRarity,
    int MinimumRelicTier,
    IReadOnlyCollection<string> Aliases);
