using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Agna Qel'a (Murders at Karlov Manor).
///
/// Land. Oracle text:
///   "This land enters tapped unless you control a basic land.
///    {T}: Add {U}.
///    {2}{U}, {T}: Draw a card, then discard a card."
///
/// Scryfall-confirmed type line: Land (non-basic, no supertype, no subtype).
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain <see cref="Land"/>, no supertype, no
///   printed subtype.
/// - <b>ETB tapped unless you control a basic land (CR 614.1c)</b> —
///   registered as a <see cref="ConditionalEntersTappedReplacement"/> on
///   the supplied <see cref="ReplacementBus"/>. Predicate: the land enters
///   untapped iff the controller controls another permanent (excluding this
///   one) that has the Basic supertype (CR 205.4a). Checks all five basic
///   land subtypes (Plains / Island / Swamp / Mountain / Forest / Wastes)
///   via <see cref="CardSupertype.Basic"/> — any basic land qualifies,
///   matching the oracle "a basic land" (not a named subtype pair).
/// - <b>{T}: Add {U}</b> — single <see cref="ManaAbility"/> producing one
///   blue pip. Standard tap-for-colour shape matching all other blue
///   producers.
/// - <b>{2}{U}, {T}: Draw a card, then discard a card.</b> — one
///   <see cref="ActivatedAbility"/> with two costs:
///   <see cref="ManaCostCost"/>("{2}{U}") + <see cref="AdditionalCost.Tap"/>
///   on the land. Resolution calls <see cref="Fx.DrawCards"/>(controller, 1)
///   then <see cref="Fx.Discard"/>(controller, 1). Mirrors the
///   draw-then-discard (rummage) shape used by InsolentNeonate, Ring 2+,
///   etc.
///
/// ## Deferred (v1 gaps)
/// - ETB-tapped replacement is omitted when constructed via the
///   single-arg dispatcher path (no <see cref="ReplacementBus"/> supplied).
///   Same posture as <see cref="CheckLandCycleFactory"/> and all other
///   ETB-replacement factories.
/// - The {2}{U} mana cost payment for the rummage ability is guarded by
///   <see cref="ManaCostCost.CanPay"/> — if the controller cannot afford
///   {2}{U} the ability is blocked from activation. The {T} guard is
///   implicit (ActivatedAbility tracks tap state via AdditionalCost.Tap).
/// </summary>
[CardName("Agna Qel'a")]
public static class AgnaQelaFactory
{
    public const string CardName = "Agna Qel'a";

    /// <summary>
    /// Fallback overload — constructs Agna Qel'a without a
    /// <see cref="ReplacementBus"/> (no ETB-tapped replacement wired).
    /// Suitable for card-shape tests and the named-card dispatch path.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Agna Qel'a owned and controlled by <paramref name="owner"/>
    /// with an optional <see cref="ReplacementBus"/> for full ETB-tapped
    /// wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "enters tapped unless
    /// you control a basic land" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB tapped unless you control a basic land (CR 614.1c).
        // Predicate returns true ⇒ enters untapped when the controller
        // controls any permanent with the Basic supertype (excluding self).
        // "Any basic land" — Plains/Island/Swamp/Mountain/Forest/Wastes
        // all qualify via CardSupertype.Basic (CR 205.4a).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    controller.Zones.Battlefield.GetCards()
                        .Any(c => !ReferenceEquals(c, self)
                                  && c.HasSupertype(CardSupertype.Basic))));
        }

        // ----------------------------------------------------------------
        // {T}: Add {U}
        // CR 605.1 — basic land mana ability, no stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        // ----------------------------------------------------------------
        // {2}{U}, {T}: Draw a card, then discard a card.
        // CR 602 — activated ability goes on the stack.
        // Cost shape: ManaCostCost("{2}{U}") + AdditionalCost.Tap.
        // Resolution: Fx.DrawCards then Fx.Discard (rummage).
        // ----------------------------------------------------------------
        var rummageEffect = new Effect(
            $"{CardName}: draw a card, then discard a card",
            () =>
            {
                var controller = land.Controller ?? owner;
                Fx.DrawCards(controller, 1);
                Fx.Discard(controller, 1);
            });

        var rummageAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}{U}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { rummageEffect });

        land.AddAbility(rummageAbility);

        return land;
    }
}
