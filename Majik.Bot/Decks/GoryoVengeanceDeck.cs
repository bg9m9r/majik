namespace Majik.Bot.Decks;

/// <summary>
/// Goryo's Vengeance archetype — Modern reanimator combo. Discard a
/// legendary fatty (Atraxa, Grand Unifier / Griselbrand / Emrakul, the
/// Aeons Torn / Archon of Cruelty) with Faithless Looting / Collective
/// Brutality / discard outlets, then Goryo's Vengeance or Through the
/// Breach to put it onto the battlefield. Discard-disruption package
/// (Thoughtseize, Inquisition, Collective Brutality) covers the combo
/// turn. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Goryo's Vengeance, current
/// snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class GoryoVengeanceDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (12)
        "Atraxa, Grand Unifier", "Atraxa, Grand Unifier", "Atraxa, Grand Unifier", "Atraxa, Grand Unifier",
        "Griselbrand", "Griselbrand", "Griselbrand",
        "Emrakul, the Aeons Torn", "Emrakul, the Aeons Torn",
        "Archon of Cruelty", "Archon of Cruelty", "Archon of Cruelty",

        // Spells (25)
        "Goryo's Vengeance", "Goryo's Vengeance", "Goryo's Vengeance", "Goryo's Vengeance",
        "Through the Breach", "Through the Breach", "Through the Breach", "Through the Breach",
        "Faithless Looting", "Faithless Looting", "Faithless Looting", "Faithless Looting",
        "Thoughtseize", "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Inquisition of Kozilek", "Inquisition of Kozilek", "Inquisition of Kozilek",
        "Persist", "Persist", "Persist",
        "Collective Brutality", "Collective Brutality", "Collective Brutality",
        "Fatal Push", "Fatal Push", "Fatal Push",

        // Lands (20)
        "Mountain",
        "Swamp",
        "Blood Crypt", "Blood Crypt", "Blood Crypt",
        "Bloodstained Mire", "Bloodstained Mire", "Bloodstained Mire", "Bloodstained Mire",
        "Marsh Flats", "Marsh Flats",
        "Verdant Catacombs",
        "Wooded Foothills", "Wooded Foothills",
        "Blackcleave Cliffs", "Blackcleave Cliffs", "Blackcleave Cliffs", "Blackcleave Cliffs",
        "Otawara, Soaring City",
        "Takenuma, Abandoned Mire",
    };
}
