namespace Majik.Bot.Decks;

/// <summary>
/// Gruul Broodscale archetype — Modern RG combo. Basking Broodscale + Blade
/// of the Bloodchief makes infinite Eldrazi Spawn, fed to Glaring Fleshraker
/// (damage) or Walking Ballista (ping) to win; Ancient Stirrings + Malevolent
/// Rumble dig, Kozilek's Command + Emrakul give a fair top-end. Urza's Saga
/// tutors a combo piece. Sideboard NOT wired in v1.
///
/// Source: mtgdecks.net Gruul Broodscale (Hunter Ovington, 1st place) — the
/// Eldrazi-Temple ramp build; seed-validated against the embedded pool (all
/// 60 resolve).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class GruulBroodscaleDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (18)
        "Basking Broodscale", "Basking Broodscale", "Basking Broodscale", "Basking Broodscale",
        "Glaring Fleshraker", "Glaring Fleshraker", "Glaring Fleshraker", "Glaring Fleshraker",
        "Writhing Chrysalis", "Writhing Chrysalis", "Writhing Chrysalis", "Writhing Chrysalis",
        "Walking Ballista", "Walking Ballista", "Walking Ballista", "Walking Ballista",
        "Emrakul, the Promised End", "Emrakul, the Promised End",

        // Spells (18)
        "Blade of the Bloodchief", "Blade of the Bloodchief", "Blade of the Bloodchief", "Blade of the Bloodchief",
        "Kozilek's Command", "Kozilek's Command", "Kozilek's Command", "Kozilek's Command",
        "Ancient Stirrings", "Ancient Stirrings", "Ancient Stirrings", "Ancient Stirrings",
        "Malevolent Rumble", "Malevolent Rumble", "Malevolent Rumble", "Malevolent Rumble",
        "Chromatic Star",
        "Grist, the Hunger Tide",

        // Lands (24)
        "Urza's Saga", "Urza's Saga", "Urza's Saga", "Urza's Saga",
        "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple",
        "Prismatic Vista", "Prismatic Vista", "Prismatic Vista", "Prismatic Vista",
        "Stomping Ground", "Stomping Ground", "Stomping Ground",
        "Grove of the Burnwillows", "Grove of the Burnwillows",
        "Copperline Gorge",
        "Forest", "Forest", "Forest",
        "Mountain",
        "Boseiju, Who Endures",
        "Cavern of Souls",
    };
}
