using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Castle Locthwain (Throne of Eldraine / reprints).
///
/// Land. Oracle text:
///   "Castle Locthwain enters tapped unless you control a Swamp.
///    {T}: Add {B}.
///    {1}{B}{B}, {T}: Draw a card, then you lose life equal to the number
///    of cards in your hand."
///
/// Scryfall-confirmed type line: Land (no basic supertype, no subtypes).
/// Castle Locthwain is NOT itself a Swamp.
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic Land, no supertype, no subtype.
/// - <b>ETB tapped unless you control a Swamp (CR 614.1c)</b> — registered
///   as a <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. The predicate checks whether the
///   controller controls at least one other permanent with the
///   <see cref="CardSubtype.Swamp"/> subtype (shocklands with Swamp subtype,
///   snow-covered Swamps etc. all qualify). The card itself is excluded via
///   reference equality (same shape as <see cref="CheckLandCycleFactory"/>).
///   Single-arg dispatcher path omits the replacement (shape-only posture).
/// - <b>{T}: Add {B}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{1}{B}{B}, {T}: Draw a card, then you lose life equal to the number
///   of cards in your hand.</b>
///   Modelled as an <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost("{1}{B}{B}"), AdditionalCost.Tap(self)]</c>.
///   Resolution:
///     1. <see cref="Fx.DrawCards"/> — draws 1 card (the new card is now in
///        hand before the life-loss count, per "then" wording CR 700.2).
///     2. <see cref="Fx.LoseLife"/> — controller loses life equal to
///        <c>controller.Zones.Hand.GetCards().Count()</c> evaluated AFTER
///        the draw. The "then" connector in the oracle text is authoritative
///        (CR 700.2): the drawn card is already in hand when life loss fires,
///        so it is counted.
///
/// ## Deferred (v1 gaps)
/// - The {1}{B}{B} mana cost is paid through the standard
///   <see cref="ManaCostCost"/> path; no colour-identity gate beyond the
///   ability's cost is wired (correct for v1 — the game rules do not
///   prevent activating an ability you cannot afford at announce time).
/// </summary>
[CardName("Castle Locthwain")]
public static class CastleLocthwainFactory
{
    public const string CardName = "Castle Locthwain";

    private static readonly ManaCost ActivationManaCost = ManaCost.Parse("{1}{B}{B}");

    /// <summary>
    /// Construct Castle Locthwain without a <see cref="ReplacementBus"/>
    /// wired. The ETB-tapped-unless-Swamp predicate is omitted (shape-only
    /// posture); the mana ability and draw ability are still attached.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Castle Locthwain.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the
    /// "enters tapped unless you control a Swamp" replacement is registered
    /// (CR 614.1c). May be null.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic Land — no supertype, no subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB tapped unless you control a Swamp (CR 614.1c).
        //
        // Predicate: entersUntappedIf returns true ⟺ the controller
        // controls at least one land (other than this card) with the
        // CardSubtype.Swamp subtype. Reference-equality exclusion of self
        // mirrors CheckLandCycleFactory's single-type predicate shape.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    controller.Zones.Battlefield.GetCards()
                        .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Swamp))));
        }

        // ----------------------------------------------------------------
        // {T}: Add {B} — vanilla mana ability (CR 605.1).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        // ----------------------------------------------------------------
        // {1}{B}{B}, {T}: Draw a card, then you lose life equal to the
        // number of cards in your hand.
        //
        // CR 602 — ordinary activated ability. Cost = {1}{B}{B} mana + tap
        // self. Resolution is sequenced by "then" (CR 700.2):
        //   1. Draw 1 card — the drawn card enters the controller's hand
        //      before step 2, so it counts toward life loss.
        //   2. Lose life equal to controller.Zones.Hand.Count (post-draw).
        //
        // The effect lambda captures `land` (not `owner`) so live
        // controller tracking via land.Controller picks up control-change
        // effects at resolution time.
        // ----------------------------------------------------------------
        var drawAndPayEffect = new Effect(
            $"{CardName}: draw a card, then lose life equal to cards in hand",
            () =>
            {
                var controller = land.Controller ?? owner;
                // Step 1: draw 1 (per "then", this happens first).
                Fx.DrawCards(controller, 1);
                // Step 2: life loss equals hand size AFTER the draw
                // (CR 700.2 — "then" sequences draw before life loss).
                var handCount = controller.Zones.Hand.GetCards().Count();
                Fx.LoseLife(controller, handCount);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}{B}{B}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { drawAndPayEffect }));

        return land;
    }
}
