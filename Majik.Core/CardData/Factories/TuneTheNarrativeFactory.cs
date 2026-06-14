using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tune the Narrative (Aetherdrift, {U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-14):
///   "Draw a card. You get {E}{E} (two energy counters)."
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {U} (mana value 1). The base
///   card shape (name / Instant type / {U} cost) is materialised from the
///   embedded JSON definition (<c>tune-the-narrative.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="BeholdTheMultiverseFactory"/>.
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/>), sequenced
///   left-to-right exactly as printed (CR 608.2c):
///   - <b>Draw a card (CR 121.1)</b> — routed through
///     <see cref="Fx.DrawCards"/> so any active replacement effect (Dredge
///     etc.) gets a shot at the draw; a library that is empty at draw time
///     flags the SBA-driven loss (CR 704.5b) via
///     <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> inside Fx
///     without throwing.
///   - <b>You get {E}{E} (CR 107.16 / CR 122.1 energy counters)</b> —
///     <see cref="Player.GainEnergy"/> adds two energy counters to the
///     caster's player-scoped energy ledger. The gain is routed through the
///     player's counter pipeline, so a "can't get counters" replacement
///     (Solemnity / Suncleanser — CR 614 / CR 122.4) still suppresses it.
///     Same energy-generation primitive used by
///     <see cref="GuideOfSoulsFactory"/>'s ETB trigger (which gains one).
///
/// ## Deferred (v1 gaps)
/// - None card-specific. Both halves reuse pre-existing primitives
///   (<see cref="Fx.DrawCards"/> + <see cref="Player.GainEnergy"/>); there
///   is no new engine mechanic here.
///
/// Rules cited:
/// - CR 117.5 — printed mana cost.
/// - CR 121.1 — draw a card.
/// - CR 107.16 / CR 122 — energy counters (a player resource).
/// - CR 704.5b — draw-from-empty-library loss.
/// </summary>
[CardName("Tune the Narrative")]
public static class TuneTheNarrativeFactory
{
    public const string CardName = "Tune the Narrative";
    public const string PrintedManaCost = "{U}";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "tune-the-narrative";

    private const int DrawAmount = 1;
    private const int EnergyGained = 2;

    /// <summary>
    /// Build Tune the Narrative from the embedded JSON and return the
    /// Instant shape. The "draw a card, then get {E}{E}" resolve body is
    /// built on demand via <see cref="BuildResolveEffect"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build Tune the Narrative's resolve effect — draw a card, then the
    /// caster gets two energy counters (CR 121.1 then CR 122, sequenced
    /// left-to-right per CR 608.2c).
    /// </summary>
    /// <param name="caster">Tune the Narrative's controller; draws the card
    /// and receives the energy.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Tune the Narrative: draw a card, then you get {E}{E}.", () =>
            {
                // CR 121.1 — "Draw a card." Route through Fx.DrawCards so a
                // ReplacementBus (Dredge etc.) gets a shot; an empty library
                // flags the SBA-driven loss (CR 704.5b) inside Fx without
                // throwing.
                Fx.DrawCards(caster, DrawAmount);

                // CR 107.16 / CR 122 — "You get {E}{E}." Two energy counters
                // land on the caster's player-scoped energy ledger (routed
                // through the counter pipeline so "can't get counters"
                // replacements still suppress it).
                caster.GainEnergy(EnergyGained);
            }),
        };
    }
}
