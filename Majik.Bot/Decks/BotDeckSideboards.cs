namespace Majik.Bot.Decks;

/// <summary>
/// 15-card Modern sideboards, one per bot archetype, keyed by the same
/// archetype id <see cref="BotDeckCatalog"/> uses for the mainboards.
///
/// <para>Each list is a realistic, on-color current-Modern sideboard
/// (graveyard hate, board wipes, free interaction, artifact/enchantment
/// answers, hate bears, wish targets, etc.). <b>Every name resolves in the
/// embedded card seed</b> — enforced by <c>DeckBindingAuditTests</c>, which
/// audits sideboards alongside mainboards.</para>
///
/// <para>Two roles are served by the sideboard zone in the engine:</para>
/// <list type="bullet">
///   <item><b>Sideboarding</b> (between games) — not modeled in v1 single-game
///   matches, but the list is the source of truth when it lands.</item>
///   <item><b>Wishboard / companion</b> — wish-tutor effects ("a card you own
///   from outside the game": Wish, Burning Wish, Karn, the Great Creator's
///   -2, etc.) and nominated companions read from
///   <see cref="Majik.Core.Players.Player.Wishboard"/> (CR 408), which is
///   physically the sideboard zone. The wish targets in these lists (e.g.
///   RubyStorm's Grapeshot / Empty the Warrens for its two maindeck Wishes,
///   EldraziTron's Karn wishboard) are therefore live the moment the bot's
///   sideboard is populated at match start.</item>
/// </list>
///
/// <para>Coverage: all <c>~24</c> archetypes are filled.
/// <see cref="BotDeckCatalog.GetSideboard"/> returns
/// <see cref="System.Array.Empty{T}"/> for any archetype absent from this
/// map, so adding a new archetype without a sideboard is a clean empty
/// wishboard, never a crash.</para>
/// </summary>
internal static class BotDeckSideboards
{
    /// <summary>Archetype id → 15-card sideboard. Names are seed-verified.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ByArchetype =
        new Dictionary<string, IReadOnlyList<string>>
        {
            // Boros Burn — race-proofing + reach hate + creature answers.
            ["Burn"] = new[]
            {
                "Smash to Smithereens", "Smash to Smithereens",
                "Deflecting Palm", "Deflecting Palm",
                "Kor Firewalker", "Kor Firewalker",
                "Roiling Vortex", "Roiling Vortex",
                "Rest in Peace", "Rest in Peace",
                "Path to Exile", "Path to Exile",
                "Brotherhood's End",
                "Wear // Tear", "Wear // Tear",
            },

            // Izzet Prowess — counters, bounce, graveyard + artifact hate.
            ["Prowess"] = new[]
            {
                "Mystical Dispute", "Mystical Dispute",
                "Spell Pierce", "Spell Pierce",
                "Abrade", "Abrade",
                "Echoing Truth", "Echoing Truth",
                "Anger of the Gods", "Anger of the Gods",
                "Surgical Extraction", "Surgical Extraction",
                "Blood Moon",
                "Engineered Explosives", "Engineered Explosives",
            },

            // Boros Energy — sweepers, hate bears, graveyard + enchantment hate.
            ["BorosEnergy"] = new[]
            {
                "Path to Exile", "Path to Exile",
                "Wear // Tear", "Wear // Tear",
                "Rest in Peace", "Rest in Peace",
                "Kataki, War's Wage",
                "Deflecting Palm", "Deflecting Palm",
                "Brotherhood's End", "Brotherhood's End",
                "Pithing Needle", "Pithing Needle",
                "Sanctifier en-Vec", "Sanctifier en-Vec",
            },

            // Golgari Yawgmoth — discard, edicts, artifact/enchantment + GY hate.
            ["Yawg"] = new[]
            {
                "Thoughtseize", "Thoughtseize",
                "Fatal Push", "Fatal Push",
                "Sheoldred's Edict", "Sheoldred's Edict",
                "Nature's Claim", "Nature's Claim",
                "Endurance", "Endurance",
                "Plague Engineer", "Plague Engineer",
                "Pick Your Poison",
                "Toxic Deluge", "Toxic Deluge",
            },

            // Affinity (artifacts) — counters, bounce, artifact-mirror + GY hate.
            ["Affinity"] = new[]
            {
                "Galvanic Blast", "Galvanic Blast",
                "Metallic Rebuke", "Metallic Rebuke",
                "Hurkyl's Recall", "Hurkyl's Recall",
                "Ceremonious Rejection", "Ceremonious Rejection",
                "Soul-Guide Lantern", "Soul-Guide Lantern",
                "Welding Jar", "Welding Jar",
                "Spell Pierce", "Spell Pierce",
                "Dispel",
            },

            // Ruby Storm — Wish targets (CR 408 wishboard) + interaction.
            ["RubyStorm"] = new[]
            {
                "Grapeshot",
                "Empty the Warrens",
                "Pyromancer Ascension",
                "Goblin Bombardment",
                "Echoing Truth", "Echoing Truth",
                "Abrade", "Abrade",
                "Pyroclasm", "Pyroclasm",
                "Roiling Vortex", "Roiling Vortex",
                "Brotherhood's End",
                "Blood Moon", "Blood Moon",
            },

            // Belcher (Boros ritual combo) — protection + sweepers + hate answers.
            ["Belcher"] = new[]
            {
                "Pyroclasm", "Pyroclasm",
                "Abrade", "Abrade",
                "Smash to Smithereens", "Smash to Smithereens",
                "Roiling Vortex", "Roiling Vortex",
                "Deflecting Palm", "Deflecting Palm",
                "Wear // Tear", "Wear // Tear",
                "Rest in Peace", "Rest in Peace",
                "Brotherhood's End",
            },

            // Goryo's Vengeance (Esper reanimator) — discard, counters, GY hate.
            ["GoryoVengeance"] = new[]
            {
                "Thoughtseize", "Thoughtseize",
                "Force of Negation", "Force of Negation",
                "Prismatic Ending", "Prismatic Ending",
                "Fatal Push", "Fatal Push",
                "Surgical Extraction", "Surgical Extraction",
                "Faerie Macabre", "Faerie Macabre",
                "Echoing Truth", "Echoing Truth",
                "Dispel",
            },

            // Living End (Temur cascade) — free counters, GY hate, sweeper hate.
            ["LivingEnd"] = new[]
            {
                "Force of Negation", "Force of Negation",
                "Subtlety", "Subtlety",
                "Endurance", "Endurance",
                "Faerie Macabre", "Faerie Macabre",
                "Force of Vigor", "Force of Vigor",
                "Brazen Borrower", "Brazen Borrower",
                "Mystic Snake",
                "Anger of the Gods", "Anger of the Gods",
            },

            // Eldrazi Tron (colorless prison) — Karn wishboard toolbox.
            ["EldraziTron"] = new[]
            {
                "Walking Ballista",
                "Wurmcoil Engine",
                "Ratchet Bomb",
                "The Stone Brain",
                "Liquimetal Coating",
                "Pithing Needle",
                "Cursed Totem",
                "Ceremonious Rejection", "Ceremonious Rejection",
                "Warping Wail", "Warping Wail",
                "Dismember", "Dismember",
                "All Is Dust",
                "Soul-Guide Lantern",
            },

            // Grixis Reanimator — discard, counters, removal, GY hate.
            ["GrixisReanimator"] = new[]
            {
                "Thoughtseize", "Thoughtseize",
                "Fatal Push", "Fatal Push",
                "Counterspell", "Counterspell",
                "Surgical Extraction", "Surgical Extraction",
                "Abrade", "Abrade",
                "Brazen Borrower", "Brazen Borrower",
                "Anger of the Gods", "Anger of the Gods",
                "Blood Moon",
            },

            // Dimir Midrange — counters, removal, discard, GY hate.
            ["DimirMidrange"] = new[]
            {
                "Counterspell", "Counterspell",
                "Mystical Dispute", "Mystical Dispute",
                "Fatal Push", "Fatal Push",
                "Go for the Throat", "Go for the Throat",
                "Thoughtseize", "Thoughtseize",
                "Surgical Extraction", "Surgical Extraction",
                "Damnation", "Damnation",
                "Dispel",
            },

            // Eldrazi Ramp (green splash) — colorless answers + GY hate.
            ["EldraziRamp"] = new[]
            {
                "Warping Wail", "Warping Wail",
                "Dismember", "Dismember",
                "Ceremonious Rejection", "Ceremonious Rejection",
                "Force of Vigor", "Force of Vigor",
                "Endurance", "Endurance",
                "Walking Ballista",
                "All Is Dust",
                "Boseiju, Who Endures",
                "Veil of Summer", "Veil of Summer",
            },

            // Neobrand (turn-1 combo) — free counters + protection + hate.
            ["Neobrand"] = new[]
            {
                "Force of Negation", "Force of Negation",
                "Veil of Summer", "Veil of Summer",
                "Endurance", "Endurance",
                "Force of Vigor", "Force of Vigor",
                "Subtlety", "Subtlety",
                "Faerie Macabre", "Faerie Macabre",
                "Mystic Snake",
                "Nature's Claim", "Nature's Claim",
            },

            // Esper Blink (UWB value) — free interaction, discard, answers.
            ["EsperBlink"] = new[]
            {
                "Solitude", "Solitude",
                "Subtlety", "Subtlety",
                "Thoughtseize", "Thoughtseize",
                "Prismatic Ending", "Prismatic Ending",
                "Force of Negation", "Force of Negation",
                "Rest in Peace", "Rest in Peace",
                "Wrath of God",
                "Dovin's Veto", "Dovin's Veto",
            },

            // Sultai Midrange (BUG) — counters, removal, free elementals + GY hate.
            ["SultaiMidrange"] = new[]
            {
                "Force of Negation", "Force of Negation",
                "Subtlety", "Subtlety",
                "Endurance", "Endurance",
                "Fatal Push", "Fatal Push",
                "Go for the Throat", "Go for the Throat",
                "Veil of Summer", "Veil of Summer",
                "Toxic Deluge", "Toxic Deluge",
                "Boseiju, Who Endures",
            },

            // Mono-Black Midrange (Necrodominance) — discard, edicts, GY hate.
            ["MonoBlackMidrange"] = new[]
            {
                "Thoughtseize", "Thoughtseize",
                "Duress", "Duress",
                "Sheoldred's Edict", "Sheoldred's Edict",
                "Fatal Push", "Fatal Push",
                "Go for the Throat", "Go for the Throat",
                "Toxic Deluge", "Toxic Deluge",
                "Leyline of the Void", "Leyline of the Void",
                "Damping Sphere",
            },

            // Azorius Blink (UW flicker) — counters, removal, sweepers, hate.
            ["AzoriusBlink"] = new[]
            {
                "Path to Exile", "Path to Exile",
                "Prismatic Ending", "Prismatic Ending",
                "Counterspell", "Counterspell",
                "Dovin's Veto", "Dovin's Veto",
                "Rest in Peace", "Rest in Peace",
                "Supreme Verdict", "Supreme Verdict",
                "Settle the Wreckage",
                "Disenchant", "Disenchant",
            },

            // Azorius Control (UW) — counters, sweepers, free pitch, hate.
            ["AzoriusControl"] = new[]
            {
                "Solitude", "Solitude",
                "Subtlety", "Subtlety",
                "Dovin's Veto", "Dovin's Veto",
                "Mystical Dispute", "Mystical Dispute",
                "Rest in Peace", "Rest in Peace",
                "Supreme Verdict",
                "Wrath of the Skies",
                "Celestial Purge", "Celestial Purge",
                "Disenchant",
            },

            // Boros Land Destruction (RW prison) — moon effects + answers.
            ["BorosLandDestruction"] = new[]
            {
                "Blood Moon", "Blood Moon",
                "Magus of the Moon", "Magus of the Moon",
                "Path to Exile", "Path to Exile",
                "Deflecting Palm", "Deflecting Palm",
                "Smash to Smithereens", "Smash to Smithereens",
                "Rest in Peace", "Rest in Peace",
                "Wear // Tear", "Wear // Tear",
                "Brotherhood's End",
            },

            // Rhinos (Temur cascade) — free counters, GY + artifact hate.
            ["Rhinos"] = new[]
            {
                "Force of Negation", "Force of Negation",
                "Subtlety", "Subtlety",
                "Endurance", "Endurance",
                "Force of Vigor", "Force of Vigor",
                "Mystic Snake", "Mystic Snake",
                "Brazen Borrower", "Brazen Borrower",
                "Veil of Summer", "Veil of Summer",
                "Boseiju, Who Endures",
            },

            // Domain Zoo (5c aggro) — removal, hate, free pitch elementals.
            ["DomainZoo"] = new[]
            {
                "Leyline Binding", "Leyline Binding",
                "Path to Exile", "Path to Exile",
                "Solitude", "Solitude",
                "Endurance", "Endurance",
                "Force of Vigor", "Force of Vigor",
                "Veil of Summer", "Veil of Summer",
                "Get Lost", "Get Lost",
                "Tear Asunder",
            },

            // Gruul Broodscale (RG combo) — artifact/enchantment hate + dig protection.
            ["GruulBroodscale"] = new[]
            {
                "Force of Vigor", "Force of Vigor",
                "Nature's Claim", "Nature's Claim",
                "Abrade", "Abrade",
                "Veil of Summer", "Veil of Summer",
                "Endurance", "Endurance",
                "Pick Your Poison", "Pick Your Poison",
                "Boseiju, Who Endures", "Boseiju, Who Endures",
                "Blood Moon",
            },

            // Eldrazi Broodscale (colorless combo) — colorless answers + hate.
            ["EldraziBroodscale"] = new[]
            {
                "Warping Wail", "Warping Wail",
                "Dismember", "Dismember",
                "Ceremonious Rejection", "Ceremonious Rejection",
                "Walking Ballista",
                "Haywire Mite", "Haywire Mite",
                "Soul-Guide Lantern", "Soul-Guide Lantern",
                "Pithing Needle", "Pithing Needle",
                "Chalice of the Void", "Chalice of the Void",
            },
        };
}
