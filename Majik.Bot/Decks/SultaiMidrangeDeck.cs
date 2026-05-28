namespace Majik.Bot.Decks;

/// <summary>
/// Sultai Midrange archetype — Modern BUG midrange. Psychic Frog +
/// Tarmogoyf + Orcish Bowmasters as the threat suite, Endurance and
/// Subtlety as free interaction. Discard (Thoughtseize, Inquisition)
/// + removal (Fatal Push, Abrupt Decay) + Counterspell triangle.
/// Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Sultai Midrange, current snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class SultaiMidrangeDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (18)
        "Psychic Frog", "Psychic Frog", "Psychic Frog", "Psychic Frog",
        "Orcish Bowmasters", "Orcish Bowmasters", "Orcish Bowmasters", "Orcish Bowmasters",
        "Tarmogoyf", "Tarmogoyf", "Tarmogoyf", "Tarmogoyf",
        "Endurance", "Endurance", "Endurance",
        "Subtlety", "Subtlety", "Subtlety",

        // Spells (22)
        "Thoughtseize", "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Fatal Push", "Fatal Push", "Fatal Push", "Fatal Push",
        "Abrupt Decay", "Abrupt Decay",
        "Inquisition of Kozilek", "Inquisition of Kozilek", "Inquisition of Kozilek", "Inquisition of Kozilek",
        "Counterspell", "Counterspell", "Counterspell",
        "Drown in the Loch", "Drown in the Loch",
        "Force of Negation",
        "Mishra's Bauble", "Mishra's Bauble",

        // Lands (20)
        "Verdant Catacombs", "Verdant Catacombs", "Verdant Catacombs", "Verdant Catacombs",
        "Misty Rainforest", "Misty Rainforest", "Misty Rainforest", "Misty Rainforest",
        "Watery Grave", "Watery Grave",
        "Overgrown Tomb", "Overgrown Tomb",
        "Breeding Pool",
        "Underground Mortuary",
        "Hedge Maze",
        "Forest",
        "Island",
        "Swamp",
        "Boseiju, Who Endures",
        "Otawara, Soaring City",
    };
}
