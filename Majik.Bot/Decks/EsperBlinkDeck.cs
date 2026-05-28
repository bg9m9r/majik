namespace Majik.Bot.Decks;

/// <summary>
/// Esper Blink archetype — Modern UWB value-blink. Ephemerate /
/// Phelia, Exuberant Shepherd repeatedly flicker Solitude, Subtlety,
/// Charming Prince, Spirited Companion, Stoneforge Mystic for incremental
/// advantage. Path to Exile + Prismatic Ending + Counterspell + Force of
/// Negation defends the engine. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Esper Blink, current snapshot).
/// MTGGoldfish lists this slot as "Esper GenericBlink" — the deck file
/// uses the colloquial "EsperBlink" registry key.
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class EsperBlinkDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (20)
        "Solitude", "Solitude", "Solitude", "Solitude",
        "Subtlety", "Subtlety", "Subtlety",
        "Phelia, Exuberant Shepherd", "Phelia, Exuberant Shepherd", "Phelia, Exuberant Shepherd", "Phelia, Exuberant Shepherd",
        "Charming Prince", "Charming Prince", "Charming Prince",
        "Spirited Companion", "Spirited Companion",
        "Skrelv, Defector Mite", "Skrelv, Defector Mite",
        "Stoneforge Mystic", "Stoneforge Mystic",

        // Spells (20)
        "Ephemerate", "Ephemerate", "Ephemerate", "Ephemerate",
        "Path to Exile", "Path to Exile", "Path to Exile", "Path to Exile",
        "Prismatic Ending", "Prismatic Ending", "Prismatic Ending",
        "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Counterspell", "Counterspell", "Counterspell",
        "Force of Negation", "Force of Negation",
        "Mana Tithe",

        // Lands (20)
        "Flooded Strand", "Flooded Strand", "Flooded Strand", "Flooded Strand",
        "Marsh Flats", "Marsh Flats", "Marsh Flats",
        "Hallowed Fountain", "Hallowed Fountain",
        "Watery Grave", "Watery Grave",
        "Godless Shrine", "Godless Shrine",
        "Plains", "Plains",
        "Island",
        "Swamp",
        "Otawara, Soaring City",
        "Eiganjo, Seat of the Empire",
        "Raffine's Tower",
    };
}
