using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Street Wraith (Future Sight, {3}{B}{B}).
///
/// Creature — Zombie 3/4. Oracle text:
///   "Swampwalk (This creature can't be blocked as long as defending
///    player controls a Swamp.)
///    Cycling—Pay 2 life. (Pay 2 life, Discard this card: Draw a card.)"
///
/// Street Wraith's "free" cycling (no mana cost — only "Pay 2 life" +
/// the implicit "Discard this card" of every Cycling ability per
/// CR 702.32a) is the printed identity of the card; the body's P/T and
/// Swampwalk keyword exist mostly so it has a creature shape if it ever
/// reaches the battlefield via Reanimator-style cheats.
///
/// ## Implemented (v1)
/// - <b>Creature — Zombie</b> 3/4 {3}{B}{B} with owner / controller wiring.
/// - <b>Swampwalk</b> as a <see cref="KeywordAbility"/> marker (CR 702.13
///   landwalk variant; <see cref="Majik.Core.Combat.CombatAbilities"/>
///   consumers gate the "can't be blocked" predicate on whether the
///   defending player controls a Swamp).
/// - <b>Cycling — Pay 2 life</b> (CR 702.32). Modeled as an
///   <see cref="ActivatedAbility"/> activated from hand, with two costs:
///   <see cref="PayLifeCost"/>(2) + <see cref="DiscardSelfCost"/>. The
///   discard-self cost gates activation to the controller's hand
///   (CR 702.32a "...activate only while [card] is in your hand"); the
///   pay-life cost gates on <c>LifeTotal &gt;= 2</c> (CR 119.4). On
///   resolution the effect draws one card via <see cref="Fx.DrawCards"/>
///   (empty library is silent — SBAs handle CR 704.5b loss). Mirrors the
///   <see cref="FaerieMacabreFactory"/> / <see cref="ChannelLandCycleFactory"/>
///   activated-from-hand cost-stack shape.
///
/// ## Why not <see cref="Majik.Core.Keywords.CyclingAbility"/>?
/// The legacy <see cref="Majik.Core.Keywords.CyclingAbility"/> is a self-
/// contained MVP that bypasses the stack — it's the pre-engine wiring
/// from Phase 14. The activated-ability rail is the canonical surface
/// (CR 702.32a explicitly defines Cycling as an activated ability that
/// goes on the stack), and routing through <see cref="ActivatedAbility"/>
/// gives Street Wraith proper interaction with cycling-trigger cards
/// (Astral Slide, Decree of Justice triggers, etc.) once those land.
///
/// ## Deferred (v1 gaps)
/// - <b>Cycling trigger surface</b> (CR 702.32b — "Whenever you cycle a
///   card...") not yet wired engine-wide. When that primitive lands, the
///   cycling activation here will publish the engine-level "cycled" event
///   automatically (the activation runs through the standard ability-
///   activation flow which all event-driven trigger consumers subscribe
///   to). No additional per-factory wiring required.
/// - <b>Cycling-as-cycling alt-cost marker</b>: cycling is technically a
///   keyword ability whose activated form happens to discard the card.
///   The <see cref="KeywordAbility"/> marker for "Cycling" isn't attached
///   here because no consumer keys on it yet; add when Cycling-trigger
///   parsers ship (same posture as <see cref="ChannelLandCycleFactory"/>).
/// </summary>
[CardName("Street Wraith")]
public static class StreetWraithFactory
{
    public const string CardName = "Street Wraith";
    public const string PrintedManaCost = "{3}{B}{B}";
    public const int Power = 3;
    public const int Toughness = 4;
    public const int CyclingLifeCost = 2;

    /// <summary>
    /// Construct Street Wraith. The cycling activated ability is attached
    /// to the card shape; activation is gated to the controller's hand by
    /// <see cref="DiscardSelfCost.CanPay"/> and to <c>LifeTotal &gt;= 2</c>
    /// by <see cref="PayLifeCost.CanPay"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Zombie });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Swampwalk — CR 702.13 (landwalk). KeywordAbility marker only;
        // CombatAbilities consumers gate the "can't be blocked" predicate
        // on whether the defending player controls a Swamp.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Swampwalk", card, owner));

        // ----------------------------------------------------------------
        // Cycling — Pay 2 life. (CR 702.32)
        //   "Pay 2 life, Discard this card: Draw a card."
        // Activated from hand: costs = [PayLifeCost(2), DiscardSelfCost];
        // effect = draw a card via Fx.DrawCards. The DiscardSelfCost
        // moves the card Hand → Graveyard during cost payment (CR 702.32a),
        // so the card is already in the graveyard when the draw resolves.
        // ----------------------------------------------------------------
        var draw = new Effect(
            $"{CardName}: cycling — draw a card",
            () => Fx.DrawCards(owner, 1));

        var cycling = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new PayLifeCost(CyclingLifeCost),
                new DiscardSelfCost(card),
            },
            effects: new IEffect[] { draw });

        card.AddAbility(cycling);

        return card;
    }
}
