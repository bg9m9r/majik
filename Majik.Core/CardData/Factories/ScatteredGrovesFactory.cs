using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scattered Groves (Amonkhet "bicycle land" cycle /
/// reprints). Oracle text (verified against Scryfall):
///
/// <code>
/// ({T}: Add {G} or {W}.)
/// This land enters tapped.
/// Cycling {2} ({2}, Discard this card: Draw a card.)
/// </code>
///
/// Type line: <c>Land — Forest Plains</c>.
///
/// Mirrors the <see cref="SavaiTriomeFactory"/> shape (tapped land +
/// cycling) but produces two colours instead of three and cycles for
/// generic <c>{2}</c> rather than <c>{3}</c>. The cycling + enters-tapped
/// abilities are not expressible in the data-only
/// <see cref="Majik.Core.CardData.Definitions.CardDefinition"/> schema yet,
/// so — like the triome cycle — identity, mana, the tapped replacement,
/// and cycling are wired in code here.
///
/// ## Implemented
/// - <b>Land — Forest Plains</b> (CR 305.6 — the printed land subtypes;
///   not Basic). Both subtypes are set on the card.
/// - <b>{T}: Add {G} or {W}</b> — two separate vanilla
///   <see cref="ManaAbility"/> instances (CR 605.1 — mana abilities don't
///   use the stack). Declared inline so the dispatcher / shape tests see
///   one ability per produced colour without an active
///   <see cref="Majik.Core.Effects.ContinuousEffectsService"/>.
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional "This
///   land enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Shape-only path (no bus) skips the
///   registration, mirroring <see cref="SavaiTriomeFactory"/>. On the
///   production load path the unconditional tapped clause is also matched
///   by <see cref="Majik.Core.CardData.EntersTappedBinder"/> off the
///   oracle text.
/// - <b>Cycling {2}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>"2"</c>). When the bus is supplied,
///   cycling resolve publishes <see cref="CardCycledEvent"/> (CR 702.32d
///   "Whenever a player cycles a card") so Lightning Rift / Astral Slide /
///   Decree of Justice triggers fire.
/// </summary>
[CardName("Scattered Groves")]
public static class ScatteredGrovesFactory
{
    /// <summary>
    /// Construct Scattered Groves owned and controlled by
    /// <paramref name="owner"/>. Single-arg path — no bus wiring (shape
    /// observability only; enters-tapped is omitted and cycling does not
    /// publish <see cref="CardCycledEvent"/>).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Scattered Groves with full bus wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve
    /// publishes <see cref="CardCycledEvent"/> against (CR 702.32d).</param>
    /// <param name="replacements">Optional replacement bus the
    /// enters-tapped restriction (CR 614.1c) is registered against.</param>
    public static Land Create(
        Player owner,
        IEventBus? eventBus,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            "Scattered Groves",
            supertypes: null,
            subtypes: new[] { CardSubtype.Forest, CardSubtype.Plains });
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Enters-tapped — CR 614.1c. Unconditional "This land enters
        // tapped." Shape-only path (no ReplacementBus) skips registration;
        // same posture as SavaiTriomeFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {G} or {W}. CR 605.1 — two mana abilities (no stack).
        // One ManaAbility per produced colour.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        // ----------------------------------------------------------------
        // Cycling {2}. CR 702.32 — "{2}, Discard this card: Draw a card."
        // Cycle cost is generic ManaCostCost("2"); the primitive appends
        // the DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d).
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost("2"), eventBus);

        return land;
    }
}
