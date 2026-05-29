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
/// Named-card factory for Desert of the Glorified (Hour of Devastation) —
/// the black member of the HOU monocolour "Desert of the …" cycling
/// tap-land cycle.
///
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {B}.
///    Cycling {1}{B} ({1}{B}, Discard this card: Draw a card.)"
///
/// Type line: <c>Land — Desert</c>.
///
/// Same shape as <see cref="StripedRiverwinderFactory"/> (cycling routed
/// through <see cref="CyclingFactory.Build"/>) layered onto the
/// enters-tapped + mana-ability land surface of the Onslaught cycling-land
/// cycle (<see cref="OnslaughtCyclingLandFactory"/>). The only structural
/// difference from the Onslaught cycle is the cycling cost: <c>{1}{B}</c>
/// (generic + colour) rather than a single coloured pip.
///
/// ## Implemented
///
/// - <b>Land — Desert</b> (CR 205.3i — Desert is a land subtype). NOT a
///   basic land; the printed mana ability is declared inline.
/// - <b>Enters-tapped replacement</b> (CR 614.1c) — unconditional
///   "This land enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Shape-only path (no bus) skips the
///   registration, mirroring the Onslaught cycle's posture.
/// - <b>{T}: Add {B}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1 —
///   mana abilities don't use the stack).
/// - <b>Cycling {1}{B}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{1}{B}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, appends the <see cref="DiscardSelfCost"/> hand-zone
///   gate (CR 702.32a), and on resolve publishes
///   <see cref="CardCycledEvent"/> for CR 702.32d subscribers
///   (Lightning Rift, Drake Haven, the Hollow One / cascade chains).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Cycling attached
///   without an event bus (no CardCycledEvent publication); enters-tapped
///   omitted. Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?, ReplacementBus?)"/> — fully
///   wired.
/// </summary>
[CardName("Desert of the Glorified")]
public static class DesertOfTheGlorifiedFactory
{
    public const string CardName = "Desert of the Glorified";

    /// <summary>Produced mana — CR 605.1, {T}: Add {B}.</summary>
    public const string ProducedMana = "B";

    /// <summary>Cycling cost — CR 702.32, {1}{B}.</summary>
    public const string CyclingCost = "{1}{B}";

    /// <summary>
    /// Construct Desert of the Glorified, card shape only — no event bus,
    /// no enters-tapped replacement registration. Cycling activation is
    /// gated to the controller's hand by <see cref="DiscardSelfCost.CanPay"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Desert of the Glorified with full bus wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve
    /// publishes <see cref="CardCycledEvent"/> against (CR 702.32d).</param>
    /// <param name="replacements">Optional replacement bus the
    /// enters-tapped restriction (CR 614.1c) is registered against.</param>
    public static Land Create(Player owner, IEventBus? eventBus, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: new[] { CardSubtype.Desert });
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // "This land enters tapped." — CR 614.1c, unconditional.
        // Shape-only path (no ReplacementBus) skips registration; matches
        // the Onslaught cycling-land cycle posture.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {B}. CR 605.1 — mana ability (no stack).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(ProducedMana)));

        // ----------------------------------------------------------------
        // Cycling {1}{B}. CR 702.32 — "{1}{B}, Discard this card: Draw a
        // card." The primitive appends the DiscardSelfCost hand-zone gate
        // (CR 702.32a) and the CardCycledEvent publish (CR 702.32d).
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost(CyclingCost), eventBus);

        return land;
    }
}
