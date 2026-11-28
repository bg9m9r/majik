using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Stack;

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
/// <summary>Why a spell was countered.</summary>
public enum SpellCounterReason
{
    /// <summary>CR 701.6a — a spell or ability's "counter" effect countered it.</summary>
    CounteredByEffect,

    /// <summary>CR 608.2b — on resolution every one of its targets was
    /// illegal, so the rules countered it ("fizzled"). No spell or ability did.</summary>
    AllTargetsIllegal,
}

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

    /// <summary>
    /// The spell or ability whose resolving effect countered
    /// <see cref="CounteredSpell"/> (Counterspell, Mystic Snake's trigger,
    /// Voidslime), when known: the object the stack was resolving
    /// (<see cref="Majik.Core.Stack.Stack.CurrentlyResolving"/>). Null when the
    /// rules countered it (<see cref="SpellCounterReason.AllTargetsIllegal"/>,
    /// CR 608.2b) or the counter happened outside a tracked resolution.
    /// </summary>
    public IStackObject? CounteringObject { get; init; }

    /// <summary>The stack entry id <see cref="CounteringObject"/> had (its
    /// StackObjectDto.Id). Null exactly when <see cref="CounteringObject"/> is.</summary>
    public Guid? CounteringEntryId { get; init; }

    /// <summary>Why the spell was countered.</summary>
    public SpellCounterReason Reason { get; init; } = SpellCounterReason.CounteredByEffect;

    public SpellCounteredEvent(ISpell counteredSpell, Player? counteringController)
        : base()
    {
        CounteredSpell = counteredSpell ?? throw new ArgumentNullException(nameof(counteredSpell));
        CounteringController = counteringController;
    }
}
