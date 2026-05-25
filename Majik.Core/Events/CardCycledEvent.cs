using Majik.Core.Cards;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a card's Cycling activated ability resolves
/// (CR 702.32). Published by <see cref="Majik.Core.Keywords.CyclingFactory"/>
/// after the activated ability's resolve effect runs (cost paid + card
/// discarded + replacement card drawn), so subscribers see the
/// post-resolve game state.
///
/// <para>
/// This is the surface "Whenever a player cycles a card" triggers
/// (Lightning Rift, Astral Slide, Astral Drift, Decree of Justice, etc.)
/// subscribe to. The triggering player is the controller of the cycled
/// card; the cycled card itself is carried so card-specific predicates
/// ("when you cycle a creature card", "when you cycle a land card") can
/// inspect the card's printed types.
/// </para>
///
/// <para>
/// CR 702.32d — "Some cards with cycling have abilities that trigger when
/// they're cycled. 'When you cycle [this card]' and 'Cycled' are
/// functionally equivalent. The triggered ability is put on the stack on
/// top of the cycling activated ability and so resolves first." Modelled
/// here by publishing AFTER the cycling resolve body runs (the trigger
/// then queues onto an empty / nearly-empty stack); full
/// CR 702.32d resolution-order semantics tracked separately when the
/// per-card "Cycled" trigger ships (e.g. Decree of Justice).
/// </para>
/// </summary>
public class CardCycledEvent : GameEvent
{
    /// <summary>The card that was cycled (now in its owner's graveyard).</summary>
    public ICard Card { get; }

    /// <summary>The player who cycled the card.</summary>
    public Players.Player Player { get; }

    public CardCycledEvent(ICard card, Players.Player player)
        : base(EventType.CardCycled)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
