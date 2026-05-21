namespace Majik.Bot.Decks;

/// <summary>
/// Boros Burn archetype — Modern-style aggressive burn. 1-drop creatures
/// (Goblin Guide, Eidolon, Swiftspear) + direct-damage spells aimed face.
/// Race plan: opponent's life is the only resource that matters.
/// Sideboard NOT wired in v1.
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class BurnDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (12)
        "Eidolon of the Great Revel", "Eidolon of the Great Revel", "Eidolon of the Great Revel", "Eidolon of the Great Revel",
        "Goblin Guide", "Goblin Guide", "Goblin Guide", "Goblin Guide",
        "Monastery Swiftspear", "Monastery Swiftspear", "Monastery Swiftspear", "Monastery Swiftspear",

        // Spells (28)
        "Boltwave", "Boltwave", "Boltwave", "Boltwave",
        "Boros Charm", "Boros Charm", "Boros Charm", "Boros Charm",
        "Lava Spike", "Lava Spike", "Lava Spike", "Lava Spike",
        "Lightning Bolt", "Lightning Bolt", "Lightning Bolt", "Lightning Bolt",
        "Searing Blaze", "Searing Blaze", "Searing Blaze", "Searing Blaze",
        "Skewer the Critics", "Skewer the Critics", "Skewer the Critics", "Skewer the Critics",
        "Skullcrack", "Skullcrack", "Skullcrack", "Skullcrack",

        // Lands (20)
        "Arid Mesa",
        "Barbarian Ring", "Barbarian Ring", "Barbarian Ring", "Barbarian Ring",
        "Bloodstained Mire",
        "Elegant Parlor",
        "Inspiring Vantage", "Inspiring Vantage", "Inspiring Vantage",
        "Mountain", "Mountain", "Mountain",
        "Sacred Foundry",
        "Scalding Tarn",
        "Sunbaked Canyon", "Sunbaked Canyon", "Sunbaked Canyon", "Sunbaked Canyon",
        "Wooded Foothills",
    };
}
