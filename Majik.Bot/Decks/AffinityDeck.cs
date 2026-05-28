namespace Majik.Bot.Decks;

/// <summary>
/// Affinity archetype — Modern artifact aggro. Cost-reduced artifact
/// creatures (Frogmite, Myr Enforcer, Sojourner's Companion) fueled by
/// Springleaf Drum, equipped with Cranial Plating, with Galvanic Blast
/// + Thoughtcast riding the artifact count. Indestructible-artifact
/// manabase (Darksteel Citadel, the bridges, Glimmervoid, Spire of
/// Industry). Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Affinity, current snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class AffinityDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (24)
        "Frogmite", "Frogmite", "Frogmite", "Frogmite",
        "Memnite", "Memnite", "Memnite", "Memnite",
        "Ornithopter", "Ornithopter", "Ornithopter", "Ornithopter",
        "Myr Enforcer", "Myr Enforcer", "Myr Enforcer", "Myr Enforcer",
        "Sojourner's Companion", "Sojourner's Companion", "Sojourner's Companion", "Sojourner's Companion",
        "Gingerbrute", "Gingerbrute", "Gingerbrute", "Gingerbrute",

        // Spells / artifacts (18)
        "Cranial Plating", "Cranial Plating", "Cranial Plating", "Cranial Plating",
        "Springleaf Drum", "Springleaf Drum", "Springleaf Drum", "Springleaf Drum",
        "Thoughtcast", "Thoughtcast", "Thoughtcast", "Thoughtcast",
        "Galvanic Blast", "Galvanic Blast", "Galvanic Blast", "Galvanic Blast",
        "Welding Jar", "Welding Jar",

        // Lands (18)
        "Darksteel Citadel", "Darksteel Citadel", "Darksteel Citadel", "Darksteel Citadel",
        "Glimmervoid", "Glimmervoid", "Glimmervoid", "Glimmervoid",
        "Spire of Industry", "Spire of Industry", "Spire of Industry", "Spire of Industry",
        "Mistvault Bridge", "Mistvault Bridge",
        "Razortide Bridge", "Razortide Bridge",
        "Thornglint Bridge", "Thornglint Bridge",
    };
}
