namespace Majik.Bot.Decks;

/// <summary>
/// Ruby Storm archetype — Modern combo built around Birgi, God of
/// Storytelling and Ruby Medallion. Cast a chain of cheap red spells,
/// generate floating mana off Birgi / Medallion / rituals, dig with
/// Reckless Impulse + Burning Inquiry, then close with Past in Flames
/// + Grapeshot (or assemble lethal Lightning Bolt copies via Galvanic
/// Iteration). Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Ruby Storm, current snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class RubyStormDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (8)
        "Birgi, God of Storytelling", "Birgi, God of Storytelling", "Birgi, God of Storytelling", "Birgi, God of Storytelling",
        "Goblin Electromancer", "Goblin Electromancer", "Goblin Electromancer", "Goblin Electromancer",

        // Spells / artifacts (38)
        "Ruby Medallion", "Ruby Medallion", "Ruby Medallion", "Ruby Medallion",
        "Manamorphose", "Manamorphose", "Manamorphose", "Manamorphose",
        "Desperate Ritual", "Desperate Ritual", "Desperate Ritual", "Desperate Ritual",
        "Pyretic Ritual", "Pyretic Ritual", "Pyretic Ritual", "Pyretic Ritual",
        "Burning Inquiry", "Burning Inquiry", "Burning Inquiry", "Burning Inquiry",
        "Reckless Impulse", "Reckless Impulse", "Reckless Impulse", "Reckless Impulse",
        "Past in Flames", "Past in Flames",
        "Grapeshot", "Grapeshot",
        "Wishclaw Talisman", "Wishclaw Talisman",
        "Galvanic Iteration", "Galvanic Iteration",
        "Reforge the Soul", "Reforge the Soul",
        "Lightning Bolt", "Lightning Bolt", "Lightning Bolt", "Lightning Bolt",

        // Lands (14)
        "Mountain", "Mountain", "Mountain", "Mountain", "Mountain", "Mountain",
        "Spirebluff Canal", "Spirebluff Canal", "Spirebluff Canal", "Spirebluff Canal",
        "Scalding Tarn", "Scalding Tarn",
        "Bloodstained Mire", "Bloodstained Mire",
    };
}
