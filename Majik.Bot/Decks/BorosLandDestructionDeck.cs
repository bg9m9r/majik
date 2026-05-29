namespace Majik.Bot.Decks;

/// <summary>
/// Boros Land Destruction archetype — Modern RW Ponza/prison. Cheap LD
/// (Stone Rain, Molten Rain, Pillage, Boom // Bust) + Fulminator Mage strip
/// the opponent's mana; Blood Moon / Magus of the Moon lock nonbasics;
/// Wrenn and Six + Nahiri + Seasoned Pyromancer grind; Umezawa's Jitte
/// (fetched by Urza's Saga) closes. Sideboard NOT wired in v1.
///
/// Source: mtgdecks.net — Aidan Trotter (RCQ Top 4, 2026-05); seed-validated
/// against the embedded pool (all 60 resolve). Wasteland → Ghost Quarter
/// (in-seed nonbasic-denial analogue).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class BorosLandDestructionDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (10)
        "Fulminator Mage", "Fulminator Mage", "Fulminator Mage",
        "Seasoned Pyromancer", "Seasoned Pyromancer", "Seasoned Pyromancer",
        "Magus of the Moon", "Magus of the Moon",
        "Goblin Dark-Dwellers", "Goblin Dark-Dwellers",

        // Planeswalkers (4)
        "Nahiri, the Harbinger", "Nahiri, the Harbinger",
        "Wrenn and Six", "Wrenn and Six",

        // Spells (24)
        "Lightning Bolt", "Lightning Bolt", "Lightning Bolt", "Lightning Bolt",
        "Molten Rain", "Molten Rain", "Molten Rain", "Molten Rain",
        "Boom // Bust", "Boom // Bust", "Boom // Bust",
        "Pillage", "Pillage",
        "Stone Rain", "Stone Rain",
        "Blood Moon", "Blood Moon", "Blood Moon",
        "Pyroclasm", "Pyroclasm",
        "Wrath of the Skies", "Wrath of the Skies",
        "Umezawa's Jitte", "Umezawa's Jitte",

        // Lands (22)
        "Sacred Foundry", "Sacred Foundry", "Sacred Foundry", "Sacred Foundry",
        "Arid Mesa", "Arid Mesa", "Arid Mesa", "Arid Mesa",
        "Inspiring Vantage", "Inspiring Vantage", "Inspiring Vantage", "Inspiring Vantage",
        "Urza's Saga", "Urza's Saga",
        "Ghost Quarter", "Ghost Quarter",
        "Mountain", "Mountain", "Mountain", "Mountain",
        "Plains", "Plains",
    };
}
