namespace Majik.Bot.Decks;

/// <summary>
/// Esper Blink archetype — Modern UWB value-blink. Ephemerate / Phelia,
/// Exuberant Shepherd flicker Solitude / Overlord of the Balemurk / Quantum
/// Riddler / Witch Enchanter for nonstop card advantage; Emperor of Bones
/// reanimates the most-recent looted creature. Teferi, Time Raveler locks
/// combo; Thoughtseize + Fatal Push + Prismatic Ending defend. Sideboard
/// NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class EsperBlinkDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Boggart Trawler",
        "Emperor of Bones", "Emperor of Bones", "Emperor of Bones",
        "Ephemerate", "Ephemerate", "Ephemerate",
        "Fatal Push", "Fatal Push", "Fatal Push",
        "Flickerwisp",
        "Flooded Strand", "Flooded Strand", "Flooded Strand", "Flooded Strand",
        "Godless Shrine", "Godless Shrine",
        "Hallowed Fountain",
        "March of Otherworldly Light",
        "Marsh Flats", "Marsh Flats", "Marsh Flats", "Marsh Flats",
        "Meticulous Archive",
        "Overlord of the Balemurk", "Overlord of the Balemurk", "Overlord of the Balemurk", "Overlord of the Balemurk",
        "Phelia, Exuberant Shepherd", "Phelia, Exuberant Shepherd", "Phelia, Exuberant Shepherd", "Phelia, Exuberant Shepherd",
        "Plains", "Plains",
        "Polluted Delta",
        "Prismatic Ending", "Prismatic Ending",
        "Quantum Riddler", "Quantum Riddler", "Quantum Riddler", "Quantum Riddler",
        "Shadowy Backstreet",
        "Solitude", "Solitude", "Solitude", "Solitude",
        "Swamp",
        "Teferi, Time Raveler", "Teferi, Time Raveler", "Teferi, Time Raveler",
        "Thoughtseize", "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Undercity Sewers",
        "Watery Grave",
        "Witch Enchanter", "Witch Enchanter", "Witch Enchanter", "Witch Enchanter",
    };
}
