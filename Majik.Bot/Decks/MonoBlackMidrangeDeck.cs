namespace Majik.Bot.Decks;

/// <summary>
/// Mono-Black Midrange archetype — Modern Necrodominance shell.
/// Necrodominance draws stacks of cards (Soul Spike pays life back / kills
/// out of nowhere); Sheoldred, the Apocalypse + Orcish Bowmasters + Dauthi
/// Voidwalker / Graveyard Trespasser threat suite; Fatal Push + Fell the
/// Profane + March of Wretched Sorrow + Force of Despair removal. Sideboard
/// NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class MonoBlackMidrangeDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Boggart Trawler", "Boggart Trawler", "Boggart Trawler", "Boggart Trawler",
        "Castle Locthwain", "Castle Locthwain", "Castle Locthwain",
        "Dauthi Voidwalker", "Dauthi Voidwalker",
        "Fatal Push", "Fatal Push", "Fatal Push", "Fatal Push",
        "Fell the Profane", "Fell the Profane", "Fell the Profane", "Fell the Profane",
        "Force of Despair", "Force of Despair", "Force of Despair",
        "Graveyard Trespasser", "Graveyard Trespasser",
        "Inquisition of Kozilek", "Inquisition of Kozilek",
        "March of Wretched Sorrow", "March of Wretched Sorrow", "March of Wretched Sorrow",
        "Necrodominance", "Necrodominance", "Necrodominance", "Necrodominance",
        "Orcish Bowmasters", "Orcish Bowmasters", "Orcish Bowmasters", "Orcish Bowmasters",
        "Sheoldred, the Apocalypse", "Sheoldred, the Apocalypse", "Sheoldred, the Apocalypse", "Sheoldred, the Apocalypse",
        "Soul Spike", "Soul Spike", "Soul Spike", "Soul Spike",
        "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp", "Swamp",
        "Thoughtseize", "Thoughtseize", "Thoughtseize", "Thoughtseize",
    };
}
