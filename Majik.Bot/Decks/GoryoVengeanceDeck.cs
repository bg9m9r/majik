namespace Majik.Bot.Decks;

/// <summary>
/// Goryo's Vengeance archetype — Modern Esper-reanimator combo. Pitch
/// Atraxa, Grand Unifier (or Griselbrand) via Faithful Mending /
/// Thoughtseize, return it with Goryo's Vengeance for the swing-turn.
/// Psychic Frog + Solitude + Ephemerate carry tempo while assembling.
/// Prismatic Ending + Force of Negation + Tainted Indulgence interact.
/// Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class GoryoVengeanceDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Atraxa, Grand Unifier", "Atraxa, Grand Unifier", "Atraxa, Grand Unifier", "Atraxa, Grand Unifier",
        "Breeding Pool",
        "Emperor of Bones",
        "Ephemerate", "Ephemerate", "Ephemerate", "Ephemerate",
        "Faithful Mending", "Faithful Mending",
        "Flooded Strand", "Flooded Strand", "Flooded Strand", "Flooded Strand",
        "Force of Negation", "Force of Negation", "Force of Negation",
        "Godless Shrine",
        "Goryo's Vengeance", "Goryo's Vengeance", "Goryo's Vengeance", "Goryo's Vengeance",
        "Griselbrand",
        "Hallowed Fountain",
        "Island",
        "Marsh Flats", "Marsh Flats", "Marsh Flats",
        "Meticulous Archive",
        "Plains",
        "Polluted Delta", "Polluted Delta", "Polluted Delta", "Polluted Delta",
        "Prismatic Ending", "Prismatic Ending", "Prismatic Ending",
        "Psychic Frog", "Psychic Frog", "Psychic Frog", "Psychic Frog",
        "Quantum Riddler", "Quantum Riddler", "Quantum Riddler",
        "Shadowy Backstreet",
        "Solitude", "Solitude", "Solitude", "Solitude",
        "Swamp",
        "Tainted Indulgence", "Tainted Indulgence",
        "Thoughtseize", "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Undercity Sewers",
        "Watery Grave",
    };
}
