namespace Majik.Bot.Decks;

/// <summary>
/// Domain Zoo archetype — Modern multicolor aggro. A five-color manabase
/// (fetches + shocks + triomes) maxes domain so Scion of Draco, Territorial
/// Kavu, Tribal Flames and Leyline Binding scale hard; Wild Nacatl + Ragavan
/// + Nishoba Brawler apply early pressure, Stubborn Denial protects.
/// Sideboard NOT wired in v1.
///
/// Source: mtgtop8 Domain Zoo ("The Nameless One") trimmed to a legal 60
/// post-May-2026-ban; seed-validated against the embedded pool (all 60
/// resolve). Phlage (banned) and a maybeboard Jegantha removed.
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class DomainZooDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (19)
        "Wild Nacatl", "Wild Nacatl", "Wild Nacatl", "Wild Nacatl",
        "Ragavan, Nimble Pilferer", "Ragavan, Nimble Pilferer", "Ragavan, Nimble Pilferer", "Ragavan, Nimble Pilferer",
        "Nishoba Brawler", "Nishoba Brawler", "Nishoba Brawler",
        "Territorial Kavu", "Territorial Kavu", "Territorial Kavu", "Territorial Kavu",
        "Scion of Draco", "Scion of Draco", "Scion of Draco", "Scion of Draco",

        // Spells (19)
        "Lightning Bolt", "Lightning Bolt", "Lightning Bolt", "Lightning Bolt",
        "Tribal Flames", "Tribal Flames", "Tribal Flames", "Tribal Flames",
        "Stubborn Denial", "Stubborn Denial", "Stubborn Denial",
        "Leyline Binding", "Leyline Binding", "Leyline Binding", "Leyline Binding",
        "Leyline of the Guildpact", "Leyline of the Guildpact", "Leyline of the Guildpact", "Leyline of the Guildpact",

        // Lands (22)
        "Arid Mesa", "Arid Mesa", "Arid Mesa", "Arid Mesa",
        "Wooded Foothills", "Wooded Foothills", "Wooded Foothills", "Wooded Foothills",
        "Flooded Strand", "Flooded Strand",
        "Marsh Flats",
        "Sacred Foundry",
        "Steam Vents",
        "Temple Garden",
        "Blood Crypt",
        "Godless Shrine",
        "Xander's Lounge",
        "Lush Portico",
        "Indatha Triome",
        "Arena of Glory",
        "Mountain",
        "Plains",
    };
}
