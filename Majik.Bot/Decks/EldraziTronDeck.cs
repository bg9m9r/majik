namespace Majik.Bot.Decks;

/// <summary>
/// Eldrazi Tron archetype — Modern colorless ramp/prison hybrid.
/// Assemble Tron (Urza's Mine + Power Plant + Tower) and/or Eldrazi
/// Temple to slam Matter Reshaper, Thought-Knot Seer, Reality Smasher,
/// and Devourer of Destiny ahead of curve. Chalice of the Void locks
/// out cheap interaction; Karn, the Great Creator wishboard plan.
/// Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Eldrazi Tron, current snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class EldraziTronDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (20)
        "Matter Reshaper", "Matter Reshaper", "Matter Reshaper", "Matter Reshaper",
        "Thought-Knot Seer", "Thought-Knot Seer", "Thought-Knot Seer", "Thought-Knot Seer",
        "Reality Smasher", "Reality Smasher", "Reality Smasher", "Reality Smasher",
        "Endurance", "Endurance",
        "Walking Ballista", "Walking Ballista",
        "Devourer of Destiny", "Devourer of Destiny", "Devourer of Destiny", "Devourer of Destiny",

        // Planeswalkers (4)
        "Karn, the Great Creator", "Karn, the Great Creator", "Karn, the Great Creator", "Karn, the Great Creator",

        // Spells / artifacts (12)
        "Chalice of the Void", "Chalice of the Void", "Chalice of the Void", "Chalice of the Void",
        "Expedition Map", "Expedition Map", "Expedition Map", "Expedition Map",
        "Talisman of Resilience", "Talisman of Resilience",
        "Relic of Progenitus", "Relic of Progenitus",

        // Lands (24)
        "Urza's Mine", "Urza's Mine", "Urza's Mine", "Urza's Mine",
        "Urza's Power Plant", "Urza's Power Plant", "Urza's Power Plant", "Urza's Power Plant",
        "Urza's Tower", "Urza's Tower", "Urza's Tower", "Urza's Tower",
        "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple",
        "Wastes", "Wastes", "Wastes", "Wastes",
        "Cavern of Souls", "Cavern of Souls",
        "Sanctum of Ugin", "Sanctum of Ugin",
    };
}
