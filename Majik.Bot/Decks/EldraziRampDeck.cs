namespace Majik.Bot.Decks;

/// <summary>
/// Eldrazi Ramp archetype — Modern colorless Eldrazi with green ramp
/// splash. Utopia Sprawl + Talisman of Impulse + Ugin's Labyrinth power out
/// Sowing Mycospawn / Writhing Chrysalis early, then Emrakul, the Promised
/// End / Sire of Seven Deaths / Ugin, Eye of the Storms close. Kozilek's
/// Command / Malevolent Rumble dig and disrupt. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class EldraziRampDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Bojuka Bog",
        "Cavern of Souls",
        "Commercial District",
        "Devourer of Destiny", "Devourer of Destiny",
        "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple",
        "Emrakul, the Promised End", "Emrakul, the Promised End", "Emrakul, the Promised End",
        "Forest", "Forest", "Forest",
        "Ghost Quarter",
        "Kozilek's Command", "Kozilek's Command", "Kozilek's Command", "Kozilek's Command",
        "Kozilek's Return", "Kozilek's Return",
        "Malevolent Rumble", "Malevolent Rumble", "Malevolent Rumble", "Malevolent Rumble",
        "Mutable Explorer",
        "Shifting Woodland",
        "Sire of Seven Deaths", "Sire of Seven Deaths", "Sire of Seven Deaths",
        "Sowing Mycospawn", "Sowing Mycospawn", "Sowing Mycospawn", "Sowing Mycospawn",
        "Stomping Ground",
        "Talisman of Impulse", "Talisman of Impulse", "Talisman of Impulse", "Talisman of Impulse",
        "Ugin's Labyrinth", "Ugin's Labyrinth", "Ugin's Labyrinth", "Ugin's Labyrinth",
        "Ugin, Eye of the Storms",
        "Utopia Sprawl", "Utopia Sprawl", "Utopia Sprawl", "Utopia Sprawl",
        "Verdant Catacombs", "Verdant Catacombs",
        "Wooded Foothills", "Wooded Foothills", "Wooded Foothills",
        "World Breaker", "World Breaker",
        "Writhing Chrysalis", "Writhing Chrysalis", "Writhing Chrysalis", "Writhing Chrysalis",
    };
}
