using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// CR 701.40e — event fired when a permanent (creature) explores. Published
/// by <see cref="Majik.Core.Primitives.Fx.Explore"/> /
/// <see cref="Majik.Core.Keywords.ExploreAction"/> AFTER the explore action
/// fully resolves (top card revealed; if a land it is in the controller's
/// hand, otherwise a +1/+1 counter is on the exploring creature and the
/// revealed card is back on top of the library or in the graveyard), so
/// subscribers observe the post-resolve state.
///
/// <para>
/// This is the surface "Whenever a creature you control explores" /
/// "Whenever ~ explores" triggers subscribe to — most notably Wildgrowth
/// Walker ("Whenever a creature you control explores, put a +1/+1 counter on
/// this creature and you gain 3 life"). The triggering player is the
/// exploring creature's controller (CR 701.40a — "its controller").
/// </para>
///
/// <para>
/// CR 701.40a — "Certain abilities instruct a permanent to explore. To do so,
/// that permanent's controller reveals the top card of their library, then
/// puts that card into their hand if it's a land card. Otherwise, that player
/// puts a +1/+1 counter on the exploring permanent and may put the revealed
/// card into their graveyard." Published once per explore resolution; an
/// effect that explores multiple times (Jadelight Ranger — "explores, then it
/// explores again") publishes one event per explore.
/// </para>
/// </summary>
public class CreatureExploredEvent : GameEvent
{
    /// <summary>The permanent (creature) that explored (CR 701.40a).</summary>
    public ICard Creature { get; }

    /// <summary>The exploring permanent's controller — the player who revealed
    /// the top card and who any "you" clause on a payoff resolves for
    /// (CR 701.40a).</summary>
    public Player Controller { get; }

    /// <summary>The card revealed off the top of the library, or
    /// <see langword="null"/> when the library was empty (CR 701.40d — "If
    /// no cards are in that library, … that permanent's controller puts a
    /// +1/+1 counter on the exploring permanent"). Subscribers can inspect
    /// the card's current zone to tell where it ended up (hand if a land,
    /// otherwise library-top or graveyard).</summary>
    public ICard? RevealedCard { get; }

    /// <summary>Whether the revealed card was a land (and therefore went to
    /// the controller's hand rather than triggering the +1/+1-counter branch;
    /// CR 701.40b vs CR 701.40c). <see langword="false"/> when the library was
    /// empty.</summary>
    public bool RevealedLand { get; }

    public CreatureExploredEvent(
        ICard creature, Player controller, ICard? revealedCard, bool revealedLand)
        : base()
    {
        Creature = creature ?? throw new ArgumentNullException(nameof(creature));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        RevealedCard = revealedCard;
        RevealedLand = revealedLand;
    }
}
