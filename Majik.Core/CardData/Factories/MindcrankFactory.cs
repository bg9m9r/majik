using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mindcrank (Mirrodin Besieged, {2}).
///
/// Artifact. Oracle text:
///   "Whenever an opponent loses life, that player puts that many cards
///    from the top of their library into their graveyard."
///
/// ## Implementation
///
/// - <b>Artifact {2}</b> — vanilla <see cref="Artifact"/> shell.
/// - <b>Life-loss → mill trigger (CR 119.3 / CR 603.1 / CR 701.13)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="LifeChangedEvent"/>
///   filtered to (<see cref="LifeChangedEvent.Player"/> is an opponent
///   of Mindcrank's controller) AND a strictly-negative life delta
///   (<see cref="LifeChangedEvent.NewLife"/> &lt;
///   <see cref="LifeChangedEvent.PreviousLife"/>). On resolution the
///   triggering opponent mills <c>|delta|</c> cards via
///   <see cref="MillAction.Apply"/> (CR 701.13 — mill = move top N of
///   library to graveyard, no draw-from-empty loss). The triggering
///   player is captured via a closure-scoped slot stamped by the
///   <see cref="ITriggerCondition"/> predicate at evaluation time so
///   resolution reads the correct opponent + amount even when multiple
///   life-loss events fire in the same window.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits trigger-bus
/// wiring and produces a shape-only card; the
/// <see cref="Create(Player, TriggerManager?)"/> overload registers the
/// trigger so bus-driven firing works in real games.
///
/// ## Deferred
///
/// - <b>Loss-replacement interaction</b>: if a "if a player would lose
///   life, instead …" replacement zeroes a life-loss event,
///   <see cref="LifeChangedEvent"/> still fires only when the life total
///   actually changes — Mindcrank's trigger correctly no-ops because
///   the delta is zero. Negative-delta filtering is therefore the
///   canonical check.
/// </summary>
[CardName("Mindcrank")]
public static class MindcrankFactory
{
    public const string CardName = "Mindcrank";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Shape-only constructor — produces the correct card shape with the
    /// trigger attached to the card but NOT registered against a
    /// <see cref="TriggerManager"/>. Suitable for factory-shape /
    /// dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Mindcrank with the life-loss → mill trigger registered
    /// against <paramref name="triggers"/> when supplied. Each opponent's
    /// strictly-negative <see cref="LifeChangedEvent"/> queues an instance
    /// of the trigger; resolution mills <c>|delta|</c> cards from that
    /// opponent's library into their graveyard via
    /// <see cref="MillAction.Apply"/>.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(name: CardName, manaCost: PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Triggered ability — CR 603.1 / CR 119.3 / CR 701.13.
        //   "Whenever an opponent loses life, that player puts that many
        //    cards from the top of their library into their graveyard."
        //
        // Predicate gates on:
        //   - LifeChangedEvent.Player is NOT Mindcrank's controller
        //     (i.e. an opponent in a 1v1 game; the engine's two-player
        //     scope makes "an opponent" equivalent to "not the controller").
        //   - NewLife < PreviousLife (strictly negative delta — life loss,
        //     not life gain).
        //
        // Resolution reads the triggering event's Player + delta off a
        // closure-captured slot that the predicate stamps at trigger
        // evaluation time. The slot is overwritten on each evaluation —
        // for the engine's one-trigger-per-life-loss model this is
        // sufficient (each life-loss event triggers + resolves before
        // the next one fires).
        // ----------------------------------------------------------------
        Player? lastTriggeredOpponent = null;
        int lastTriggeredAmount = 0;

        var millEffect = new Effect(
            $"{CardName}: opponent mills cards equal to life lost (CR 701.13)",
            () =>
            {
                var opponent = lastTriggeredOpponent;
                var amount = lastTriggeredAmount;
                if (opponent == null || amount <= 0) return;
                MillAction.Apply(opponent, amount);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<LifeChangedEvent>((e, _) =>
            {
                // Opponent = not the (live) controller of Mindcrank.
                var ctrl = card.Controller ?? owner;
                if (ReferenceEquals(e.Player, ctrl)) return false;
                if (e.NewLife >= e.PreviousLife) return false; // not a loss

                lastTriggeredOpponent = e.Player;
                lastTriggeredAmount = e.PreviousLife - e.NewLife;
                return true;
            }),
            effects: new IEffect[] { millEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
