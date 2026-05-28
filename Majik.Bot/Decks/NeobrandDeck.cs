namespace Majik.Bot.Decks;

/// <summary>
/// Neobrand archetype — Modern turn 1-2 combo. Pitch Allosaurus Rider
/// (or Generous Ent) free with Chancellor of the Tangle / Burning-Tree
/// Emissary mana, then Neoform / Eldritch Evolution into Griselbrand,
/// draw 14, dump Autochthon Wurm / Worldspine via Glimpse of Tomorrow,
/// finish via Laboratory Maniac mill-out. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Neobrand, current snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class NeobrandDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (22)
        "Allosaurus Rider", "Allosaurus Rider", "Allosaurus Rider", "Allosaurus Rider",
        "Autochthon Wurm",
        "Griselbrand", "Griselbrand", "Griselbrand", "Griselbrand",
        "Laboratory Maniac",
        "Burning-Tree Emissary", "Burning-Tree Emissary", "Burning-Tree Emissary", "Burning-Tree Emissary",
        "Generous Ent", "Generous Ent", "Generous Ent", "Generous Ent",
        "Chancellor of the Tangle", "Chancellor of the Tangle", "Chancellor of the Tangle", "Chancellor of the Tangle",

        // Spells (26)
        "Neoform", "Neoform", "Neoform", "Neoform",
        "Manamorphose", "Manamorphose", "Manamorphose", "Manamorphose",
        "Summoner's Pact", "Summoner's Pact", "Summoner's Pact", "Summoner's Pact",
        "Pact of Negation", "Pact of Negation",
        "Nourishing Shoal", "Nourishing Shoal",
        "Eldritch Evolution", "Eldritch Evolution", "Eldritch Evolution", "Eldritch Evolution",
        "Glimpse of Tomorrow", "Glimpse of Tomorrow",
        "Eladamri's Call", "Eladamri's Call", "Eladamri's Call", "Eladamri's Call",

        // Lands (12)
        "Forest", "Forest", "Forest", "Forest",
        "Stomping Ground", "Stomping Ground",
        "Wooded Foothills", "Wooded Foothills",
        "Verdant Catacombs", "Verdant Catacombs",
        "Misty Rainforest", "Misty Rainforest",
    };
}
