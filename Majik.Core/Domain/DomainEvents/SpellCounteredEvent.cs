using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// CR 701.5 — a spell was countered (removed from the stack and put into its
/// owner's graveyard by a "counter" effect). Published from the single
/// counter chokepoint (<see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/>)
/// whenever the removed stack object is a <see cref="ISpell"/>, so every
/// counter path — counterspells (Counterspell, Mana Leak, …), ETB counters
/// (Mystic Snake), and ability counters (Voidslime) — surfaces the same
/// signal.
///
/// <para><see cref="CounteringController"/> is the controller of the spell or
/// ability that did the countering — i.e. the player who "controls" the
/// counter. It is captured from the currently-resolving stack object's
/// controller (threaded onto the stack via
/// <see cref="Majik.Core.Stack.Stack.CurrentResolutionController"/> by the
/// resolution entry points). May be null when a counter happens outside a
/// tracked resolution (defensive — no Baral-style "you control" trigger then
/// matches).</para>
///
/// <para>Consumers: "whenever a spell or ability you control counters a
/// spell" triggers — Baral, Chief of Compliance. The predicate gates on
/// <see cref="CounteringController"/> matching the trigger's controller.</para>
/// </summary>
public class SpellCounteredEvent : GameEvent
{
    /// <summary>The spell that was countered (now in its owner's graveyard).</summary>
    public ISpell CounteredSpell { get; }

    /// <summary>
    /// The controller of the spell or ability that countered
    /// <see cref="CounteredSpell"/>. Null when the counter occurred outside a
    /// tracked resolution context.
    /// </summary>
    public Player? CounteringController { get; }

    public SpellCounteredEvent(ISpell counteredSpell, Player? counteringController)
        : base(EventType.Triggered)
    {
        CounteredSpell = counteredSpell ?? throw new ArgumentNullException(nameof(counteredSpell));
        CounteringController = counteringController;
    }
}
