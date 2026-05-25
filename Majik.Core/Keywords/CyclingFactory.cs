using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.Keywords;

/// <summary>
/// Primitive shared builder for the Cycling keyword (CR 702.32).
///
/// <para>
/// CR 702.32a — "Cycling is an activated ability that functions only
/// while the card with cycling is in a player's hand. '[Cost], Discard
/// this card: Draw a card' is the activated ability." This factory
/// attaches that activated ability to a card shape using the canonical
/// activated-from-hand surface every modern factory uses (Channel lands,
/// Faerie Macabre, Street Wraith): an <see cref="ActivatedAbility"/> with
/// cost stack <c>[cycleCost, DiscardSelfCost(card)]</c> + resolve body
/// <see cref="Fx.DrawCards"/>(1). The <see cref="DiscardSelfCost"/> hand-
/// zone gate (CR 702.32a) is the activated-from-hand surface — no
/// separate <c>ActiveZones</c> flag needed on
/// <see cref="ActivatedAbility"/>.
/// </para>
///
/// <para>
/// CR 702.32d — "Some cards with cycling have abilities that trigger when
/// they're cycled." Publishing <see cref="CardCycledEvent"/> at the tail
/// of the resolve body is the surface those triggers (Lightning Rift,
/// Astral Slide, Astral Drift, Decree of Justice) subscribe to via the
/// standard <see cref="Abilities.EventTriggerCondition{TEvent}"/>
/// machinery. The event fires after the discard + draw so the card sits
/// in its owner's graveyard and the replacement card is in hand by the
/// time the trigger evaluates — matches CR 702.32d's "the cycling
/// ability has finished resolving" timing.
/// </para>
///
/// <para>
/// Cost is <see cref="ICost"/>, not <see cref="Majik.Core.ValueObjects.ManaCost"/>.
/// Most printed cycling is mana (<see cref="ManaCostCost"/>) but
/// alt-cost cycling (Street Wraith — <see cref="PayLifeCost"/>(2),
/// typecycling, etc.) is common enough that taking an <see cref="ICost"/>
/// here means every cycling shape routes through this single builder.
/// </para>
///
/// <para>
/// This primitive co-exists with the legacy
/// <see cref="CyclingAbility"/> stack-bypass MVP. New factories should
/// route through this builder; the MVP stays for its existing tests
/// until a follow-up sweep removes it.
/// </para>
/// </summary>
public static class CyclingFactory
{
    /// <summary>
    /// Attach the Cycling activated ability + the
    /// <see cref="KeywordAbility"/> marker to <paramref name="source"/>.
    ///
    /// <para>
    /// The activated ability resolves to a single card draw for the
    /// card's owner (CR 108.4 — a card in a hand has no controller
    /// distinct from its owner; the controller of the activated ability
    /// is the player who activated it, which is the hand's owner).
    /// </para>
    ///
    /// <para>
    /// When an <paramref name="eventBus"/> is supplied the resolve body
    /// publishes a <see cref="CardCycledEvent"/> after the draw so
    /// CR 702.32d "Whenever a player cycles a card" triggers fire. Shape-
    /// only callers (no bus) get the ability attached without the
    /// publish step — same posture as
    /// <see cref="Majik.Core.CardData.Factories.BojukaBogFactory"/>'s
    /// shape-only overload.
    /// </para>
    /// </summary>
    /// <param name="source">The card the cycling ability lives on. Must
    /// have its <see cref="ICard.Owner"/> already wired — the resolve
    /// body reads <c>source.Owner</c> as the draw target.</param>
    /// <param name="cycleCost">The "[Cost]" half of "[Cost], Discard
    /// this card: Draw a card." Typically a
    /// <see cref="ManaCostCost"/> ({1}, {G}, etc.) but
    /// <see cref="PayLifeCost"/>, sacrifice-rider, etc. are all
    /// supported. Must NOT include the discard-self half — that's
    /// appended automatically.</param>
    /// <param name="eventBus">Optional event bus the resolve body
    /// publishes <see cref="CardCycledEvent"/> against. When null no
    /// event fires (shape-only path).</param>
    /// <returns>The attached <see cref="ActivatedAbility"/>, for callers
    /// that need to wire test assertions or stamp additional metadata.
    /// The ability has already been added to <paramref name="source"/>
    /// via <see cref="Card.AddAbility"/>.</returns>
    public static ActivatedAbility Build(
        ICard source,
        ICost cycleCost,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(cycleCost);
        if (source.Owner is null)
        {
            throw new ArgumentException(
                "CyclingFactory.Build: card.Owner must be set before attaching the cycling ability — the resolve body draws for the owner.",
                nameof(source));
        }

        var owner = source.Owner;

        // CR 702.32a — Cycling is a *keyword* activated ability. The
        // KeywordAbility marker exposes "Cycling" so any consumer that
        // keys on keyword presence (oracle audit, future static effects
        // that grant or remove cycling, bot decision layer) can see it
        // without scanning the activated-ability cost stack.
        source.AddAbility(new KeywordAbility("Cycling", source, owner));

        // CR 702.32a — "[Cost], Discard this card: Draw a card."
        // Cost stack: caller-supplied cycle cost + DiscardSelfCost.
        // The DiscardSelfCost provides the activated-from-hand zone gate
        // (CR 702.32a — activates only while the card is in its owner's
        // hand); DiscardSelfCost.CanPay returns false when the card is
        // anywhere else, so activation fails before the ability hits
        // the stack.
        var drawEffect = new Effect(
            $"{source.Name}: cycling — draw a card",
            () =>
            {
                Fx.DrawCards(owner, 1);
                // CR 702.32d — publish the "cycled" event AFTER the draw
                // so subscribers (Lightning Rift, Astral Slide, etc.)
                // see the post-resolve state (card in graveyard +
                // replacement in hand).
                eventBus?.Publish(new CardCycledEvent(source, owner));
            });

        var ability = new ActivatedAbility(
            source: source,
            controller: owner,
            costs: new ICost[]
            {
                cycleCost,
                new DiscardSelfCost(source),
            },
            effects: new IEffect[] { drawEffect });

        source.AddAbility(ability);
        return ability;
    }
}
