namespace Majik.Bot.Decks;

/// <summary>
/// Azorius (WU) Lotus Bloom / Whir Belcher — Modern artifact-combo control.
/// The plan: ramp into <c>Goblin Charbelcher</c> as a near-deterministic kill
/// on a manabase built almost entirely from modal-DFC back-face lands, so the
/// Belcher reveal flips through a deck with effectively zero true lands and
/// burns the opponent out in one activation. <c>Lotus Bloom</c> (suspend ritual)
/// and <c>Whir of Invention</c> (improvise artifact tutor) accelerate to and
/// fetch the Belcher; <c>Thundertrap Trainer</c> digs for the pieces.
///
/// Interaction protects the combo turn: <c>Force of Negation</c> / <c>Disrupting
/// Shoal</c> (free/pitch counters), <c>Stern Scolding</c> + <c>Sink into Stupor</c>
/// (cheap counter / bounce), and <c>Orim's Chant</c> + <c>Suppression Ray</c> to
/// tax or lock the opponent's turn while the kill assembles. <c>Preordain</c>,
/// <c>Sea Gate Restoration</c>, and <c>Tameshi, Reality Architect</c> (once-per-turn
/// noncreature-bounce → draw) supply card flow and a grindy backup plan;
/// <c>Tamiyo, Inquisitive Student</c> is a one-of value flip-planeswalker.
///
/// Manabase is the archetype's whole point: every land slot is the back face of
/// a spell MDFC — <c>Hydroelectric Specimen</c>, <c>Jwari Disruption</c>,
/// <c>Sea Gate Restoration</c>, <c>Sink into Stupor</c>, <c>Razorgrass Ambush</c>,
/// <c>Waterlogged Teachings</c> — referenced here by FRONT-FACE name (the engine
/// resolves "Front // Back" MDFCs from the front face; see
/// <see cref="Majik.Core.CardData.EmbeddedCardRepository.GetByName"/>), so the
/// Charbelcher reveal hits no true lands.
///
/// Source: MTGGoldfish Modern list 7828188 (60-card mainboard). Sideboard NOT
/// wired in v1.
///
/// Tameshi, Reality Architect is registered as a known partial implementation
/// (KnownPartialImplementations) for its activated land-from-graveyard ability;
/// its draw trigger is implemented. The deck-implementation audit treats it as a
/// registered partial, not a gap.
/// </summary>
internal static class AzoriusLotusBelcherDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
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
}
