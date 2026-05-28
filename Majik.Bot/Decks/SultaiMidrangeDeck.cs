namespace Majik.Bot.Decks;

/// <summary>
/// Sultai Midrange archetype — Modern BUG Birthing Ritual / Coiling Oracle
/// value deck. Coiling Oracle + Shardless Agent + Ice-Fang Coatl generate
/// steady tempo; Birthing Ritual sacrifices early bodies for Subtlety /
/// Endurance / Abhorrent Oculus; Flare of Denial + Force of Negation + Sink
/// into Stupor counter the combo turns. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class SultaiMidrangeDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Abhorrent Oculus", "Abhorrent Oculus", "Abhorrent Oculus", "Abhorrent Oculus",
        "Birthing Ritual", "Birthing Ritual", "Birthing Ritual", "Birthing Ritual",
        "Boseiju, Who Endures",
        "Breeding Pool",
        "Coiling Oracle", "Coiling Oracle", "Coiling Oracle", "Coiling Oracle",
        "Endurance", "Endurance", "Endurance",
        "Fblthp, the Lost",
        "Flare of Denial", "Flare of Denial", "Flare of Denial", "Flare of Denial",
        "Force of Negation", "Force of Negation",
        "Harbinger of the Seas", "Harbinger of the Seas",
        "Hedge Maze", "Hedge Maze",
        "Ice-Fang Coatl", "Ice-Fang Coatl", "Ice-Fang Coatl", "Ice-Fang Coatl",
        "Misty Rainforest", "Misty Rainforest", "Misty Rainforest", "Misty Rainforest",
        "Otawara, Soaring City",
        "Overgrown Tomb",
        "Shardless Agent", "Shardless Agent", "Shardless Agent", "Shardless Agent",
        "Sink into Stupor", "Sink into Stupor", "Sink into Stupor",
        "Snow-Covered Forest", "Snow-Covered Forest",
        "Snow-Covered Island", "Snow-Covered Island",
        "Subtlety", "Subtlety", "Subtlety", "Subtlety",
        "Verdant Catacombs", "Verdant Catacombs", "Verdant Catacombs", "Verdant Catacombs",
        "Watery Grave",
        "Witherbloom Charm", "Witherbloom Charm",
    };
}
