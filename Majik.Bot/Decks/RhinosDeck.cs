namespace Majik.Bot.Decks;

/// <summary>
/// Rhinos archetype — Modern Temur Crashing Footfalls cascade. Violent
/// Outburst (unbanned, May 2026) + Shardless Agent cascade into the
/// 0-mana Crashing Footfalls for two 4/4 trampling Rhino tokens; Force of
/// Negation / Subtlety / Fire // Ice protect the combo turn. Sideboard NOT
/// wired in v1.
///
/// Source: Card Kingdom / MTGGoldfish Temur Rhinos (post-May-2026-ban);
/// seed-validated against the embedded pool (all 60 resolve). Manabase
/// normalized to a standard Temur fetch/shock/triome base.
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class RhinosDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (7)
        "Shardless Agent", "Shardless Agent", "Shardless Agent", "Shardless Agent",
        "Quantum Riddler", "Quantum Riddler", "Quantum Riddler",

        // Spells (28)
        "Crashing Footfalls", "Crashing Footfalls", "Crashing Footfalls", "Crashing Footfalls",
        "Violent Outburst", "Violent Outburst", "Violent Outburst", "Violent Outburst",
        "Force of Negation", "Force of Negation", "Force of Negation", "Force of Negation",
        "Fire // Ice", "Fire // Ice", "Fire // Ice", "Fire // Ice",
        "Subtlety", "Subtlety", "Subtlety", "Subtlety",
        "Sink into Stupor // Soporific Springs", "Sink into Stupor // Soporific Springs",
        "Vibrance", "Vibrance",
        "Wistfulness", "Wistfulness",
        "Endurance",
        "Dismember",

        // Lands (25)
        "Misty Rainforest", "Misty Rainforest", "Misty Rainforest", "Misty Rainforest",
        "Scalding Tarn", "Scalding Tarn", "Scalding Tarn", "Scalding Tarn",
        "Wooded Foothills", "Wooded Foothills", "Wooded Foothills",
        "Ketria Triome", "Ketria Triome",
        "Breeding Pool",
        "Steam Vents",
        "Stomping Ground",
        "Boseiju, Who Endures",
        "Otawara, Soaring City",
        "Commercial District",
        "Hedge Maze",
        "Lórien Revealed",
        "Forest",
        "Island", "Island",
        "Mountain",
    };
}
