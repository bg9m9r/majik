namespace Majik.Bot.Decks;

/// <summary>
/// Dimir Midrange archetype — Modern UB tempo. Psychic Frog + Orcish
/// Bowmasters + Tasigur as the threat base, Quantum Riddler as a counter on
/// legs, Tamiyo flipping into a draw engine. Thoughtseize + Fatal Push +
/// Counterspell + Force of Negation defends. Drown in the Loch + Spell
/// Snare punish curves. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class DimirMidrangeDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Agna Qel'a",
        "Cling to Dust",
        "Consign to Memory",
        "Counterspell", "Counterspell", "Counterspell",
        "Darkslick Shores",
        "Drown in the Loch", "Drown in the Loch",
        "Fatal Push", "Fatal Push", "Fatal Push", "Fatal Push",
        "Flooded Strand", "Flooded Strand", "Flooded Strand",
        "Force of Despair", "Force of Despair",
        "Force of Negation", "Force of Negation", "Force of Negation",
        "Gloomlake Verge",
        "Island", "Island",
        "Kaito, Bane of Nightmares",
        "Marsh Flats", "Marsh Flats",
        "Orcish Bowmasters", "Orcish Bowmasters", "Orcish Bowmasters",
        "Polluted Delta", "Polluted Delta", "Polluted Delta", "Polluted Delta",
        "Psychic Frog", "Psychic Frog", "Psychic Frog", "Psychic Frog",
        "Quantum Riddler", "Quantum Riddler", "Quantum Riddler",
        "Sink into Stupor", "Sink into Stupor",
        "Spell Snare", "Spell Snare",
        "Subtlety", "Subtlety",
        "Swamp",
        "Tamiyo, Inquisitive Student", "Tamiyo, Inquisitive Student",
        "Tasigur, the Golden Fang", "Tasigur, the Golden Fang",
        "Thoughtseize", "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Undercity Sewers", "Undercity Sewers",
        "Watery Grave", "Watery Grave",
    };
}
