namespace Majik.Bot.Decks;

/// <summary>
/// Azorius Control archetype — Modern UW control. Counterspell + Prismatic
/// Ending + Supreme Verdict / Wrath of the Skies hold the fort; Solitude +
/// Subtlety give free interaction; Teferi (both) + Isochron Scepter lock the
/// game; card advantage from Consult the Star Charts / Stock Up / Lórien
/// Revealed. Sideboard NOT wired in v1.
///
/// Source: mtgtop8 — Yuri Anichini, 1st, "Modern Monster @ Dungeon Street"
/// (2026-02); seed-validated against the embedded pool (all 60 resolve).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class AzoriusControlDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (5)
        "Solitude", "Solitude", "Solitude", "Solitude",
        "Subtlety",

        // Spells (32)
        "Consult the Star Charts", "Consult the Star Charts", "Consult the Star Charts", "Consult the Star Charts",
        "Counterspell", "Counterspell", "Counterspell", "Counterspell",
        "Lórien Revealed", "Lórien Revealed",
        "Orim's Chant", "Orim's Chant", "Orim's Chant", "Orim's Chant",
        "Prismatic Ending", "Prismatic Ending", "Prismatic Ending", "Prismatic Ending",
        "Stock Up", "Stock Up",
        "Supreme Verdict", "Supreme Verdict",
        "Wrath of the Skies", "Wrath of the Skies",
        "Isochron Scepter", "Isochron Scepter",
        "Teferi, Hero of Dominaria", "Teferi, Hero of Dominaria",
        "Teferi, Time Raveler", "Teferi, Time Raveler", "Teferi, Time Raveler", "Teferi, Time Raveler",

        // Lands (23)
        "Arid Mesa", "Arid Mesa",
        "Demolition Field", "Demolition Field",
        "Flooded Strand", "Flooded Strand", "Flooded Strand", "Flooded Strand",
        "Hall of Storm Giants",
        "Hallowed Fountain", "Hallowed Fountain",
        "Island", "Island", "Island",
        "Meticulous Archive", "Meticulous Archive",
        "Monumental Henge",
        "Mystic Gate",
        "Otawara, Soaring City",
        "Plains", "Plains",
        "Steam Vents",
        "Thundering Falls",
    };
}
