namespace Majik.Core.Tests.Combo;

/// <summary>
/// Shared combo-line definitions + the canonical Azorius Lotus Belcher card
/// list for the Phase-B engine-correctness tests (plan 2026-06-13). One place
/// for the deck's card names (mirrored from
/// <c>Majik.Bot/Decks/AzoriusLotusBelcherDeck.cs</c> — the deck type is
/// <c>internal</c> to Majik.Bot and Majik.Core.Tests does not reference
/// Majik.Bot, so the list is copied here and guarded by a count check in the
/// coverage test). If the real deck changes, the coverage test's count
/// assertion fails and this list must be re-synced.
/// </summary>
public static class BelcherLines
{
    /// <summary>
    /// The exact 60-card AzoriusLotusBelcher mainboard (front-face names),
    /// copied verbatim from <c>AzoriusLotusBelcherDeck.Cards</c> (#2630).
    /// </summary>
    public static readonly IReadOnlyList<string> DeckCards = new[]
    {
        "Disrupting Shoal", "Disrupting Shoal", "Disrupting Shoal", "Disrupting Shoal",
        "Force of Negation", "Force of Negation",
        "Goblin Charbelcher", "Goblin Charbelcher", "Goblin Charbelcher", "Goblin Charbelcher",
        "Hydroelectric Specimen", "Hydroelectric Specimen", "Hydroelectric Specimen", "Hydroelectric Specimen",
        "Jwari Disruption", "Jwari Disruption", "Jwari Disruption", "Jwari Disruption",
        "Lotus Bloom", "Lotus Bloom", "Lotus Bloom", "Lotus Bloom",
        "Orim's Chant", "Orim's Chant", "Orim's Chant",
        "Preordain", "Preordain", "Preordain", "Preordain",
        "Razorgrass Ambush", "Razorgrass Ambush",
        "Sea Gate Restoration", "Sea Gate Restoration", "Sea Gate Restoration", "Sea Gate Restoration",
        "Sink into Stupor", "Sink into Stupor", "Sink into Stupor", "Sink into Stupor",
        "Stern Scolding", "Stern Scolding", "Stern Scolding",
        "Suppression Ray", "Suppression Ray", "Suppression Ray", "Suppression Ray",
        "Tameshi, Reality Architect", "Tameshi, Reality Architect", "Tameshi, Reality Architect", "Tameshi, Reality Architect",
        "Tamiyo, Inquisitive Student",
        "Thundertrap Trainer", "Thundertrap Trainer", "Thundertrap Trainer",
        "Waterlogged Teachings", "Waterlogged Teachings",
        "Whir of Invention", "Whir of Invention", "Whir of Invention", "Whir of Invention",
    };

    /// <summary>The six MDFC fronts that make up the manabase (nonland fronts,
    /// land backs). Each is nonland in the library by its front face.</summary>
    public static readonly IReadOnlyList<string> MdfcFronts = new[]
    {
        "Hydroelectric Specimen",
        "Jwari Disruption",
        "Sea Gate Restoration",
        "Sink into Stupor",
        "Razorgrass Ambush",
        "Waterlogged Teachings",
    };

    /// <summary>
    /// A library of <paramref name="count"/> MDFC fronts (cycled) — every card
    /// is nonland by its front face, so a Charbelcher reveal walks the whole
    /// library. The first 7 become the opening hand (London draw from the top).
    /// </summary>
    public static IReadOnlyList<string> MdfcFrontLibrary(int count) =>
        Enumerable.Range(0, count)
            .Select(i => MdfcFronts[i % MdfcFronts.Count])
            .ToList();
}
