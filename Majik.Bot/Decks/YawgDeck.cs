namespace Majik.Bot.Decks;

/// <summary>
/// Yawgmoth (Golgari) archetype — Modern-style sacrifice/value deck built
/// around Yawgmoth, Thran Physician's pay-1-life-sac-creature ability.
/// Engine pieces: Yawgmoth + undying creatures (Young Wolf) for endless
/// fodder; Grist + Spymaster's Vault produce sac fodder; Agatha's Soul
/// Cauldron graveyard recursion; Dredger's Insight + Malevolent Rumble
/// for card selection and lifegain triggers.
///
/// Sideboard NOT wired in v1 (BotDeckCatalog returns a single 60-card list
/// per archetype).
///
/// "Assaultron Invader" is a Secret Lair reprint of Walking Ballista — the
/// alias is resolved by <c>CardNameAliases</c> so the engine treats both
/// names as the same implemented card.
/// </summary>
internal static class YawgDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (21)
        "Assaultron Invader", "Assaultron Invader", "Assaultron Invader",
        "Badgermole Cub", "Badgermole Cub", "Badgermole Cub", "Badgermole Cub",
        "Delighted Halfling", "Delighted Halfling", "Delighted Halfling", "Delighted Halfling",
        "Endurance",
        "Orcish Bowmasters",
        "Yawgmoth, Thran Physician", "Yawgmoth, Thran Physician", "Yawgmoth, Thran Physician", "Yawgmoth, Thran Physician",
        "Young Wolf", "Young Wolf", "Young Wolf", "Young Wolf",

        // Planeswalkers (2)
        "Grist, the Hunger Tide", "Grist, the Hunger Tide",

        // Spells + artifacts + enchantments (16)
        "Agatha's Soul Cauldron", "Agatha's Soul Cauldron", "Agatha's Soul Cauldron", "Agatha's Soul Cauldron",
        "Dredger's Insight", "Dredger's Insight", "Dredger's Insight", "Dredger's Insight",
        "Green Sun's Zenith", "Green Sun's Zenith", "Green Sun's Zenith", "Green Sun's Zenith",
        "Malevolent Rumble", "Malevolent Rumble", "Malevolent Rumble", "Malevolent Rumble",

        // Lands (21)
        "Boseiju, Who Endures", "Boseiju, Who Endures",
        "Dryad Arbor",
        "Forest", "Forest", "Forest",
        "Misty Rainforest",
        "Overgrown Tomb", "Overgrown Tomb",
        "Spymaster's Vault", "Spymaster's Vault", "Spymaster's Vault",
        "Swamp",
        "Underground Mortuary",
        "Verdant Catacombs", "Verdant Catacombs", "Verdant Catacombs", "Verdant Catacombs",
        "Wastewood Verge",
        "Windswept Heath", "Windswept Heath",
    };
}
