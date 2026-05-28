namespace Majik.Bot.Decks;

/// <summary>
/// Grixis Reanimator archetype — Modern UBR reanimator midrange. Faithless
/// Looting + Thought Scour bin Archon of Cruelty; Persist / Unearth /
/// Emperor of Bones bring it back. Abhorrent Oculus delayed threat off
/// self-mill. Psychic Frog + Fatal Push + Thoughtseize hold the board.
/// Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class GrixisReanimatorDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Abhorrent Oculus", "Abhorrent Oculus", "Abhorrent Oculus",
        "Archon of Cruelty", "Archon of Cruelty", "Archon of Cruelty", "Archon of Cruelty",
        "Bitter Triumph",
        "Blood Crypt",
        "Bloodstained Mire", "Bloodstained Mire", "Bloodstained Mire", "Bloodstained Mire",
        "Emperor of Bones", "Emperor of Bones", "Emperor of Bones", "Emperor of Bones",
        "Faithless Looting", "Faithless Looting", "Faithless Looting", "Faithless Looting",
        "Fatal Push", "Fatal Push", "Fatal Push", "Fatal Push",
        "Island", "Island",
        "Persist", "Persist", "Persist", "Persist",
        "Polluted Delta", "Polluted Delta", "Polluted Delta", "Polluted Delta",
        "Prismari Charm", "Prismari Charm",
        "Psychic Frog", "Psychic Frog", "Psychic Frog", "Psychic Frog",
        "Raucous Theater",
        "Spell Pierce", "Spell Pierce",
        "Swamp", "Swamp", "Swamp",
        "Thought Scour", "Thought Scour", "Thought Scour",
        "Thoughtseize", "Thoughtseize", "Thoughtseize",
        "Troll of Khazad-dûm",
        "Undercity Sewers",
        "Unearth", "Unearth", "Unearth",
        "Watery Grave", "Watery Grave",
    };
}
