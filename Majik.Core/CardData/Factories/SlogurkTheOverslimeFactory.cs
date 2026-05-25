using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Slogurk, the Overslime (Innistrad: Crimson Vow,
/// {1}{G}{U}).
///
/// Legendary Creature — Ooze 3/3. Oracle text:
///   "Trample
///    Whenever a land card is put into your graveyard from anywhere,
///    put a +1/+1 counter on Slogurk, the Overslime.
///    Remove three +1/+1 counters from Slogurk, the Overslime: Return
///    it to its owner's hand.
///    When Slogurk, the Overslime leaves the battlefield, return up to
///    three target land cards from your graveyard to your hand."
///
/// ## Implemented (v1)
/// - 3/3 Legendary Creature — Ooze at {1}{G}{U}.
/// - Trample <see cref="KeywordAbility"/> marker (CR 702.19) — combat
///   helpers (<see cref="Majik.Core.Combat.CombatAbilities"/>) read this
///   directly the same way every other trample-bearing factory does.
/// - <b>Counter-on-land-to-graveyard trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>
///   filtered to (a) <c>e.ToZone == Graveyard</c>, (b) the card has
///   <see cref="CardType.Land"/>, and (c) the destination graveyard
///   belongs to Slogurk's controller (checked at trigger time via
///   <c>e.Card.Owner == controller</c>; the printed "your graveyard"
///   reads the card's owner since a land card's graveyard is its
///   owner's — CR 404.2). FromZone is unconstrained so the trigger
///   catches lands milled from the library, discarded from hand,
///   bounced-into-graveyard-via-replacement, sacrificed from the
///   battlefield, etc. ("from anywhere", CR 700.4 / 614). On resolve,
///   adds a single +1/+1 counter to Slogurk via
///   <see cref="Fx.PlaceCounter"/>.
/// - <b>Remove-three activated ability (CR 602)</b>: cost
///   "Remove three +1/+1 counters from Slogurk". v1 expresses the
///   counter-removal as the cost work performed inside the effect
///   body — no <c>RemoveCounterCost</c> ICost shape exists yet (same
///   posture as Priest of Fell Rites' exile-self half). The effect
///   guards the counter count before paying so insufficient-counter
///   activations are no-op-shaped. Returns Slogurk to its owner's
///   hand via <see cref="Fx.BounceToHand"/> (route through ZoneService
///   when supplied so LTB triggers / replacements fire — including
///   this card's own LTB).
/// - <b>LTB triggered ability (CR 603.6c / 603.10c)</b>: fires on
///   <see cref="CardMovedEvent"/> with FromZone=Battlefield (any
///   destination — hand, graveyard, exile, library). Returns up to
///   three target land cards from controller's graveyard to their
///   hand. v1 deterministic first-three pick (mirrors Reanimate /
///   Priest of Fell Rites target deferral); "up to three" auto-takes
///   the maximum eligible. LTB is "looks back" — active in the
///   battlefield zone per CR 603.6d so the trigger sees its own zone
///   exit.
///
/// ## Deferred (v1 gaps)
/// - <b>Counter-removal as a first-class cost</b>: no
///   <c>RemoveCounterCost</c> ICost shape yet. Counter-removal happens
///   inside the activated ability's effect body (Priest of Fell Rites
///   pattern). Lands when the cost vocabulary grows a counter-pay
///   primitive.
/// - <b>"Up to three" target prompt</b>: deterministic first-three
///   pick. Real agent-driven multi-target choice with opt-out per
///   slot awaits the prompt MVP.
/// - <b>Sorcery-speed / activation timing</b>: not enforced on the
///   activated ability — printed oracle has no "activate only as a
///   sorcery" clause, so the default instant-speed activation is
///   correct (CR 602.1).
/// </summary>
[CardName("Slogurk, the Overslime")]
public static class SlogurkTheOverslimeFactory
{
    public const string CardName = "Slogurk, the Overslime";
    public const string PrintedManaCost = "{1}{G}{U}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int BouncePerActivationCounters = 3;
    public const int LtbMaxLands = 3;

    /// <summary>
    /// Single-arg dispatcher path. Attaches Trample + all three
    /// abilities to the card shape without TriggerManager wiring.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Fully-wired construction. When <paramref name="zoneService"/> is
    /// supplied, the activated-ability bounce and the LTB land-return
    /// route through <see cref="ZoneService.MoveCard"/> so dependent
    /// triggers fire (CR 603.6a). When <paramref name="triggers"/> is
    /// supplied both triggered abilities register for bus-driven firing.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Ooze });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trample (CR 702.19) — keyword marker; combat helpers read
        // these directly.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Trample"));

        // ----------------------------------------------------------------
        // Whenever a land card is put into your graveyard from anywhere,
        // put a +1/+1 counter on Slogurk. (CR 603.1.)
        //
        // "Your graveyard" reads the destination graveyard's owner. A
        // land card moving to a graveyard always lands in its owner's
        // graveyard (CR 404.2), so the predicate gates on
        // e.Card.Owner == controller. FromZone is unconstrained — the
        // printed "from anywhere" matches Battlefield → Graveyard (sac,
        // destroy), Library → Graveyard (mill, drawn-then-discarded),
        // Hand → Graveyard (discard).
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: place a +1/+1 counter (land hit graveyard)",
            () => Fx.PlaceCounter(card, CounterType.PlusOnePlusOne));

        var counterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.ToZone == ZoneType.Graveyard
                && e.Card.HasType(CardType.Land)
                && ReferenceEquals(e.Card.Owner, owner)),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(counterTrigger);
        triggers?.RegisterTriggeredAbility(counterTrigger);

        // ----------------------------------------------------------------
        // Remove three +1/+1 counters from Slogurk: Return it to its
        // owner's hand. (CR 602.) Counter-removal is performed inside
        // the effect body (no RemoveCounterCost ICost shape at v1 —
        // Priest of Fell Rites' exile-self pattern). Mana-cost portion
        // is none (no {X}, just the counter-removal cost).
        // ----------------------------------------------------------------
        var bounceEffect = new Effect(
            $"{CardName}: remove three +1/+1 counters → bounce to owner's hand",
            () =>
            {
                // Guard the cost half — insufficient counters or
                // off-battlefield invocations are no-op-shaped while
                // the engine doesn't yet validate counter-pay costs
                // pre-activation. Same posture as Priest of Fell
                // Rites' zone-guard for its graveyard-exile-self cost.
                if (card.Zone != ZoneType.Battlefield) return;
                if (card.Counters.Count(CounterType.PlusOnePlusOne)
                    < BouncePerActivationCounters) return;

                Fx.RemoveCounter(
                    card,
                    CounterType.PlusOnePlusOne,
                    BouncePerActivationCounters);

                // CR 701.20 — bounce to owner's hand. ZoneService
                // routes the publish so LTB triggers fire — including
                // this card's own LTB (CR 603.6d / 603.10c).
                Fx.BounceToHand(card, zoneService);
            });

        var bounceAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            // Mana cost is empty; counter-removal cost is documented in
            // the description-only path (cost-primitive deferred).
            costs: Array.Empty<ICost>(),
            effects: new IEffect[] { bounceEffect });

        card.AddAbility(bounceAbility);

        // ----------------------------------------------------------------
        // When Slogurk leaves the battlefield, return up to three target
        // land cards from your graveyard to your hand. (CR 603.6c —
        // LTB; "looks back" per CR 603.10c so the ability sees its own
        // last-known-information on the battlefield.) v1 deterministic
        // first-three pick.
        // ----------------------------------------------------------------
        var ltbEffect = new Effect(
            $"{CardName}: LTB — return up to three lands from graveyard to hand",
            () =>
            {
                var lands = owner.Zones.Graveyard.GetCards()
                    .Where(c => c.HasType(CardType.Land))
                    .Take(LtbMaxLands)
                    .ToList();

                foreach (var land in lands)
                {
                    Fx.ReturnFromGraveyardToHand(land, zoneService);
                }
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                ReferenceEquals(e.Card, card)
                && e.FromZone == ZoneType.Battlefield),
            effects: new IEffect[] { ltbEffect },
            // LTB active zone is Battlefield — CR 603.6d — so the
            // trigger evaluates against last-known-information on the
            // battlefield even though by the time the event publishes
            // the card has already left.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }
}
