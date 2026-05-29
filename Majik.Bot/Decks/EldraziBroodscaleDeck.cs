namespace Majik.Bot.Decks;

/// <summary>
/// Eldrazi Broodscale archetype — Modern colorless/Eldrazi combo. Same
/// Basking Broodscale + Blade of the Bloodchief infinite-Spawn engine as the
/// Gruul build, but leans on a colorless Eldrazi-Temple shell with an
/// artifact toolbox (Springleaf Drum, Vexing Bauble, Haywire Mite,
/// Soul-Guide Lantern) for Urza's Saga to tutor. Cavern of Souls makes the
/// Eldrazi uncounterable. Sideboard NOT wired in v1.
///
/// Source: mtgdecks.net / MTGGoldfish Broodscale combo (Eldrazi-Temple
/// variant); seed-validated against the embedded pool (all 60 resolve).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class EldraziBroodscaleDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (18)
        "Basking Broodscale", "Basking Broodscale", "Basking Broodscale", "Basking Broodscale",
        "Glaring Fleshraker", "Glaring Fleshraker", "Glaring Fleshraker", "Glaring Fleshraker",
        "Writhing Chrysalis", "Writhing Chrysalis", "Writhing Chrysalis", "Writhing Chrysalis",
        "Walking Ballista", "Walking Ballista", "Walking Ballista", "Walking Ballista",
        "Emrakul, the Promised End", "Emrakul, the Promised End",

        // Spells & artifacts (20)
        "Blade of the Bloodchief", "Blade of the Bloodchief", "Blade of the Bloodchief", "Blade of the Bloodchief",
        "Kozilek's Command", "Kozilek's Command", "Kozilek's Command", "Kozilek's Command",
        "Malevolent Rumble", "Malevolent Rumble", "Malevolent Rumble", "Malevolent Rumble",
        "Ancient Stirrings", "Ancient Stirrings", "Ancient Stirrings", "Ancient Stirrings",
        "Springleaf Drum",
        "Vexing Bauble",
        "Haywire Mite",
        "Soul-Guide Lantern",

        // Lands (22)
        "Urza's Saga", "Urza's Saga", "Urza's Saga", "Urza's Saga",
        "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple",
        "Grove of the Burnwillows", "Grove of the Burnwillows", "Grove of the Burnwillows", "Grove of the Burnwillows",
        "Wooded Foothills", "Wooded Foothills", "Wooded Foothills", "Wooded Foothills",
        "Cavern of Souls", "Cavern of Souls",
        "Boseiju, Who Endures", "Boseiju, Who Endures",
        "Forest",
        "Stomping Ground",
    };
}
