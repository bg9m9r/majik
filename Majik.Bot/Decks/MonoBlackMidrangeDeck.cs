namespace Majik.Bot.Decks;

/// <summary>
/// Mono-Black Midrange archetype — Modern. Sheoldred, the Apocalypse +
/// Phyrexian Obliterator + Orcish Bowmasters + Dauthi Voidwalker close
/// games; Hostile Investigator + Sedgemoor Witch add card flow.
/// Thoughtseize / Inquisition / Liliana of the Veil deny resources;
/// Fatal Push / Bloodchief's Thirst / Damn handle the board. Sideboard
/// NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Mono-Black Midrange, current
/// snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class MonoBlackMidrangeDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (16)
        "Orcish Bowmasters", "Orcish Bowmasters", "Orcish Bowmasters", "Orcish Bowmasters",
        "Sheoldred, the Apocalypse", "Sheoldred, the Apocalypse", "Sheoldred, the Apocalypse",
        "Hostile Investigator", "Hostile Investigator", "Hostile Investigator",
        "Phyrexian Obliterator",
        "Sedgemoor Witch", "Sedgemoor Witch", "Sedgemoor Witch",
        "Dauthi Voidwalker", "Dauthi Voidwalker",

        // Planeswalkers + Spells (22)
        "Thoughtseize", "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Fatal Push", "Fatal Push", "Fatal Push", "Fatal Push",
        "Inquisition of Kozilek", "Inquisition of Kozilek", "Inquisition of Kozilek", "Inquisition of Kozilek",
        "Liliana of the Veil", "Liliana of the Veil", "Liliana of the Veil", "Liliana of the Veil",
        "Bloodchief's Thirst", "Bloodchief's Thirst",
        "Damn", "Damn",
        "Mishra's Bauble", "Mishra's Bauble",

        // Lands (22)
        "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp",
        "Urborg, Tomb of Yawgmoth", "Urborg, Tomb of Yawgmoth",
        "Takenuma, Abandoned Mire",
        "Bloodstained Mire", "Bloodstained Mire", "Bloodstained Mire", "Bloodstained Mire",
        "Marsh Flats", "Marsh Flats", "Marsh Flats",
        "Verdant Catacombs", "Verdant Catacombs",
        "Polluted Delta",
        "Cabal Coffers",
    };
}
