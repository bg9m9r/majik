namespace Majik.Bot.Decks;

/// <summary>
/// Ruby Storm archetype — Modern Ral, Monsoon Mage combo. Ral + Ruby
/// Medallion + rituals chain cheap red spells for floating mana; Wrenn's
/// Resolve, Reckless Impulse, Artist's Talent, Glimpse the Impossible dig;
/// Past in Flames + Grapeshot or Wish-board finish the turn. Sideboard NOT
/// wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class RubyStormDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Arid Mesa", "Arid Mesa", "Arid Mesa",
        "Artist's Talent", "Artist's Talent",
        "Bloodstained Mire", "Bloodstained Mire",
        "Commercial District",
        "Desperate Ritual", "Desperate Ritual", "Desperate Ritual", "Desperate Ritual",
        "Elegant Parlor",
        "Flashback", "Flashback",
        "Gemstone Caverns",
        "Glimpse the Impossible", "Glimpse the Impossible",
        "Grapeshot",
        "Manamorphose", "Manamorphose", "Manamorphose", "Manamorphose",
        "Mountain", "Mountain", "Mountain", "Mountain",
        "Past in Flames", "Past in Flames", "Past in Flames",
        "Pyretic Ritual", "Pyretic Ritual", "Pyretic Ritual", "Pyretic Ritual",
        "Ral, Monsoon Mage", "Ral, Monsoon Mage", "Ral, Monsoon Mage", "Ral, Monsoon Mage",
        "Reckless Impulse", "Reckless Impulse", "Reckless Impulse", "Reckless Impulse",
        "Ruby Medallion", "Ruby Medallion", "Ruby Medallion", "Ruby Medallion",
        "Sacred Foundry",
        "Scalding Tarn", "Scalding Tarn",
        "Sunbaked Canyon",
        "Valakut Awakening", "Valakut Awakening",
        "Wish", "Wish",
        "Wooded Foothills", "Wooded Foothills",
        "Wrenn's Resolve", "Wrenn's Resolve", "Wrenn's Resolve", "Wrenn's Resolve",
    };
}
