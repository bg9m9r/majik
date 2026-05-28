namespace Majik.Bot.Decks;

/// <summary>
/// Grixis Reanimator archetype — Modern reanimator midrange. Bin big
/// threats (Archon of Cruelty, Atraxa Grand Unifier, Emrakul Aeons Torn,
/// Griselbrand) via Faithless Looting + Thought Scour, return them with
/// Persist. Discard + Counterspell + removal package keeps the door
/// open while assembling. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Grixis Reanimator, current
/// snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class GrixisReanimatorDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (12)
        "Archon of Cruelty", "Archon of Cruelty", "Archon of Cruelty", "Archon of Cruelty",
        "Atraxa, Grand Unifier", "Atraxa, Grand Unifier", "Atraxa, Grand Unifier",
        "Emrakul, the Aeons Torn",
        "Griselbrand", "Griselbrand",
        "Subtlety", "Subtlety",

        // Spells (28)
        "Persist", "Persist", "Persist", "Persist",
        "Faithless Looting", "Faithless Looting", "Faithless Looting", "Faithless Looting",
        "Thought Scour", "Thought Scour", "Thought Scour", "Thought Scour",
        "Thoughtseize", "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Force of Negation", "Force of Negation",
        "Fatal Push", "Fatal Push", "Fatal Push",
        "Counterspell", "Counterspell", "Counterspell",
        "Inquisition of Kozilek", "Inquisition of Kozilek", "Inquisition of Kozilek", "Inquisition of Kozilek",

        // Lands (20)
        "Watery Grave", "Watery Grave",
        "Steam Vents", "Steam Vents",
        "Blood Crypt", "Blood Crypt",
        "Scalding Tarn", "Scalding Tarn", "Scalding Tarn",
        "Bloodstained Mire", "Bloodstained Mire", "Bloodstained Mire",
        "Misty Rainforest", "Misty Rainforest",
        "Verdant Catacombs",
        "Island", "Island",
        "Swamp",
        "Mountain",
        "Underground River",
    };
}
