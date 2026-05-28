namespace Majik.Bot.Decks;

/// <summary>
/// Belcher archetype — Modern Boros-touched ritual combo. Cast a chain of
/// rituals + cantrip enablers (Witch Enchanter, Pinnacle Monk, Sundering
/// Eruption) into Goblin Charbelcher activation via Irencrag Feat /
/// Stormscale Scion floors. Orim's Chant taxes the opponent's turn; Legion
/// Leadership gives a backup creature win. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class BelcherDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Desperate Ritual", "Desperate Ritual", "Desperate Ritual", "Desperate Ritual",
        "Goblin Charbelcher", "Goblin Charbelcher", "Goblin Charbelcher", "Goblin Charbelcher",
        "Irencrag Feat", "Irencrag Feat", "Irencrag Feat", "Irencrag Feat",
        "Legion Leadership", "Legion Leadership", "Legion Leadership", "Legion Leadership",
        "Manamorphose", "Manamorphose", "Manamorphose", "Manamorphose",
        "March of Reckless Joy", "March of Reckless Joy", "March of Reckless Joy",
        "Orim's Chant", "Orim's Chant", "Orim's Chant", "Orim's Chant",
        "Pinnacle Monk", "Pinnacle Monk", "Pinnacle Monk", "Pinnacle Monk",
        "Pyretic Ritual", "Pyretic Ritual", "Pyretic Ritual", "Pyretic Ritual",
        "Razorgrass Ambush",
        "Shatterskull Smashing", "Shatterskull Smashing", "Shatterskull Smashing", "Shatterskull Smashing",
        "Stormscale Scion", "Stormscale Scion", "Stormscale Scion", "Stormscale Scion",
        "Strike It Rich", "Strike It Rich", "Strike It Rich", "Strike It Rich",
        "Sundering Eruption", "Sundering Eruption", "Sundering Eruption", "Sundering Eruption",
        "Talisman of Conviction", "Talisman of Conviction", "Talisman of Conviction", "Talisman of Conviction",
        "Witch Enchanter", "Witch Enchanter", "Witch Enchanter", "Witch Enchanter",
    };
}
