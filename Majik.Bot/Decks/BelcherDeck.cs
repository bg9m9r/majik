namespace Majik.Bot.Decks;

/// <summary>
/// Belcher archetype — Modern mono-red ritual-fueled combo aimed at
/// resolving Goblin Charbelcher with zero non-Mountain lands cast yet
/// (Irencrag Feat + rituals dump six mana into Belcher activation;
/// Reforge the Soul / Burning Inquiry refill; Geological Appraiser
/// discovers more rituals). Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Belcher, current snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class BelcherDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (8)
        "Insolent Neonate", "Insolent Neonate", "Insolent Neonate", "Insolent Neonate",
        "Geological Appraiser", "Geological Appraiser", "Geological Appraiser", "Geological Appraiser",

        // Spells / artifacts (47)
        "Goblin Charbelcher", "Goblin Charbelcher", "Goblin Charbelcher", "Goblin Charbelcher",
        "Desperate Ritual", "Desperate Ritual", "Desperate Ritual", "Desperate Ritual",
        "Pyretic Ritual", "Pyretic Ritual", "Pyretic Ritual", "Pyretic Ritual",
        "Manamorphose", "Manamorphose", "Manamorphose", "Manamorphose",
        "Burning Inquiry", "Burning Inquiry", "Burning Inquiry", "Burning Inquiry",
        "Reforge the Soul", "Reforge the Soul", "Reforge the Soul", "Reforge the Soul",
        "Reckless Impulse", "Reckless Impulse", "Reckless Impulse", "Reckless Impulse",
        "Pentad Prism", "Pentad Prism", "Pentad Prism", "Pentad Prism",
        "Mishra's Bauble", "Mishra's Bauble", "Mishra's Bauble", "Mishra's Bauble",
        "Galvanic Iteration", "Galvanic Iteration",
        "Past in Flames", "Past in Flames",
        "Irencrag Feat", "Irencrag Feat", "Irencrag Feat",
        "Light Up the Stage", "Light Up the Stage", "Light Up the Stage", "Light Up the Stage",

        // Lands (5)
        "Mountain", "Mountain", "Mountain", "Mountain", "Mountain",
    };
}
