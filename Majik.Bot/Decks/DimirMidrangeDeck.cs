namespace Majik.Bot.Decks;

/// <summary>
/// Dimir Midrange archetype — Modern UB tempo/midrange. Murktide Regent
/// + Psychic Frog as the threat base, Orcish Bowmasters for board
/// presence + Bowmaster trigger, Subtlety as a free bounce/protection.
/// Discard (Thoughtseize) + removal (Fatal Push) + Counterspell hold
/// the game while threats close. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Dimir Midrange, current snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class DimirMidrangeDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (15)
        "Murktide Regent", "Murktide Regent", "Murktide Regent", "Murktide Regent",
        "Psychic Frog", "Psychic Frog", "Psychic Frog", "Psychic Frog",
        "Orcish Bowmasters", "Orcish Bowmasters", "Orcish Bowmasters", "Orcish Bowmasters",
        "Subtlety", "Subtlety", "Subtlety",

        // Spells (25)
        "Thoughtseize", "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Fatal Push", "Fatal Push", "Fatal Push", "Fatal Push",
        "Counterspell", "Counterspell", "Counterspell", "Counterspell",
        "Drown in the Loch", "Drown in the Loch", "Drown in the Loch",
        "Preordain", "Preordain", "Preordain", "Preordain",
        "Mishra's Bauble", "Mishra's Bauble", "Mishra's Bauble", "Mishra's Bauble",
        "Force of Negation", "Force of Negation",

        // Lands (20)
        "Watery Grave", "Watery Grave", "Watery Grave",
        "Underground Mortuary", "Underground Mortuary",
        "Otawara, Soaring City",
        "Takenuma, Abandoned Mire",
        "Misty Rainforest", "Misty Rainforest", "Misty Rainforest",
        "Verdant Catacombs", "Verdant Catacombs",
        "Bloodstained Mire", "Bloodstained Mire",
        "Marsh Flats", "Marsh Flats",
        "Scalding Tarn", "Scalding Tarn",
        "Polluted Delta",
        "Island",
    };
}
