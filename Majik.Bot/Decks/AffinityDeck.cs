namespace Majik.Bot.Decks;

/// <summary>
/// Affinity archetype — Modern artifact deck rebuilt around Urza's Saga +
/// Mox Opal. Saga makes 0/0 Construct tokens (huge with the artifact count)
/// and tutors Mishra's Bauble / Welding Jar / Shadowspear; Mox Opal ramps;
/// Kappa Cannoneer / Pinnacle Emissary / Krang close games; Weapons
/// Manufacturing + Claws of Gix value-engine; Engineered Explosives sweeps;
/// Metallic Rebuke + Sink into Stupor protect. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class AffinityDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Arcbound Ravager", "Arcbound Ravager",
        "Claws of Gix", "Claws of Gix", "Claws of Gix",
        "Emry, Lurker of the Loch",
        "Engineered Explosives", "Engineered Explosives", "Engineered Explosives", "Engineered Explosives",
        "Fiery Islet", "Fiery Islet", "Fiery Islet", "Fiery Islet",
        "Galvanic Blast",
        "Island", "Island",
        "Kappa Cannoneer", "Kappa Cannoneer", "Kappa Cannoneer", "Kappa Cannoneer",
        "Krang, Master Mind",
        "Metallic Rebuke", "Metallic Rebuke",
        "Mishra's Bauble", "Mishra's Bauble", "Mishra's Bauble", "Mishra's Bauble",
        "Mox Opal", "Mox Opal", "Mox Opal", "Mox Opal",
        "Pinnacle Emissary", "Pinnacle Emissary", "Pinnacle Emissary", "Pinnacle Emissary",
        "Pithing Needle",
        "Shadowspear",
        "Sink into Stupor", "Sink into Stupor",
        "Skateboard",
        "Spirebluff Canal", "Spirebluff Canal", "Spirebluff Canal", "Spirebluff Canal",
        "Steam Vents",
        "Tormod's Crypt", "Tormod's Crypt", "Tormod's Crypt", "Tormod's Crypt",
        "Urza's Saga", "Urza's Saga", "Urza's Saga", "Urza's Saga",
        "Weapons Manufacturing", "Weapons Manufacturing", "Weapons Manufacturing", "Weapons Manufacturing",
        "Welding Jar", "Welding Jar",
    };
}
