namespace Majik.Bot.Decks;

/// <summary>
/// Izzet Prowess archetype — Modern-style tempo deck. Cheap creatures
/// with prowess + cantrips + burn. Sideboard NOT wired in v1.
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class ProwessDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (16)
        "Cori-Steel Cutter", "Cori-Steel Cutter", "Cori-Steel Cutter", "Cori-Steel Cutter",
        "Dragon's Rage Channeler", "Dragon's Rage Channeler", "Dragon's Rage Channeler", "Dragon's Rage Channeler",
        "Monastery Swiftspear", "Monastery Swiftspear", "Monastery Swiftspear", "Monastery Swiftspear",
        "Slickshot Show-Off", "Slickshot Show-Off", "Slickshot Show-Off", "Slickshot Show-Off",

        // Spells / artifacts (26)
        "Expressive Iteration", "Expressive Iteration", "Expressive Iteration",
        "Faithless Looting", "Faithless Looting",
        "Lava Dart", "Lava Dart", "Lava Dart", "Lava Dart",
        "Lightning Bolt", "Lightning Bolt", "Lightning Bolt", "Lightning Bolt",
        "Mishra's Bauble", "Mishra's Bauble", "Mishra's Bauble", "Mishra's Bauble",
        "Mutagenic Growth", "Mutagenic Growth", "Mutagenic Growth", "Mutagenic Growth",
        "Preordain", "Preordain", "Preordain", "Preordain",
        "Violent Urge",

        // Lands (18)
        "Arid Mesa", "Arid Mesa",
        "Bloodstained Mire", "Bloodstained Mire", "Bloodstained Mire",
        "Fiery Islet", "Fiery Islet",
        "Mountain", "Mountain", "Mountain",
        "Scalding Tarn", "Scalding Tarn",
        "Steam Vents", "Steam Vents", "Steam Vents",
        "Thundering Falls",
        "Wooded Foothills", "Wooded Foothills",
    };
}
