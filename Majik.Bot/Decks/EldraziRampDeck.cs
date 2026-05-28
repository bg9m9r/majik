namespace Majik.Bot.Decks;

/// <summary>
/// Eldrazi Ramp archetype — Modern colorless Eldrazi ramp. Sanctum
/// Weaver + Eldrazi Temple + Sowing Mycospawn accelerate into Kozilek's
/// Command, Thought-Knot Seer, then big finishers (Ulamog, the Ceaseless
/// Hunger / Emrakul, the Promised End). Chalice of the Void shores up
/// the slow-start angle. Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Eldrazi Ramp, current snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class EldraziRampDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (22)
        "Eldrazi Mimic", "Eldrazi Mimic", "Eldrazi Mimic", "Eldrazi Mimic",
        "Matter Reshaper", "Matter Reshaper",
        "Thought-Knot Seer", "Thought-Knot Seer", "Thought-Knot Seer", "Thought-Knot Seer",
        "Reality Smasher", "Reality Smasher",
        "Devourer of Destiny", "Devourer of Destiny", "Devourer of Destiny", "Devourer of Destiny",
        "Sanctum Weaver", "Sanctum Weaver", "Sanctum Weaver", "Sanctum Weaver",
        "Ulamog, the Ceaseless Hunger", "Ulamog, the Ceaseless Hunger",

        // Spells (10)
        "Kozilek's Command", "Kozilek's Command", "Kozilek's Command", "Kozilek's Command",
        "Chalice of the Void", "Chalice of the Void", "Chalice of the Void", "Chalice of the Void",
        "Sowing Mycospawn", "Sowing Mycospawn", "Sowing Mycospawn", "Sowing Mycospawn",

        // Top end (2) — kept separately; counts above already include creature totals
        "Emrakul, the Promised End", "Emrakul, the Promised End",

        // Lands (22)
        "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple",
        "Cavern of Souls", "Cavern of Souls", "Cavern of Souls",
        "Boseiju, Who Endures", "Boseiju, Who Endures",
        "Wastes", "Wastes", "Wastes", "Wastes", "Wastes", "Wastes",
        "The Mycosynth Gardens", "The Mycosynth Gardens",
        "Sanctum of Ugin", "Sanctum of Ugin",
        "Forest", "Forest", "Forest",
        "Karplusan Forest", "Karplusan Forest",
    };
}
