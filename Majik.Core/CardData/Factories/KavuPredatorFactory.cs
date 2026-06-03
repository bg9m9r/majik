using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kavu Predator (Apocalypse / reprints, {1}{G}).
///
/// Creature — Kavu 2/2. Oracle text (Scryfall):
///   "Trample
///    Whenever an opponent gains life, put that many +1/+1 counters on this
///    creature."
///
/// ## Shape source
/// Kavu Predator is built in code (no JSON identity file) — same posture as
/// <see cref="VitoThornOfTheDuskRoseFactory"/>, whose "that much" lifegain
/// amount this card mirrors but in the OPPONENT-gains-life direction. It is the
/// first card to exercise the declarative-sibling helper
/// <see cref="Triggers.OnLifeGainedByOpponent"/> (the opponent-scoped mirror of
/// <see cref="Triggers.OnLifeGainedByPlayer"/>); the matching JSON variant is
/// <c>whenever_an_opponent_gains_life</c>
/// (<see cref="Majik.Core.CardData.Definitions.WheneverAnOpponentGainsLifeTriggerDef"/>).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Kavu (CR 205.3m) at {1}{G}, owner / controller wired.
/// - <b>Trample (CR 702.19)</b>: <see cref="KeywordAbility"/> marker.
/// - <b>Lifegain-punish trigger (CR 603.6a / CR 119.3 / CR 109.5 / CR 603.7)</b>:
///   "Whenever an opponent gains life, put that many +1/+1 counters on this
///   creature." Wired via <see cref="Triggers.OnLifeGainedByOpponent"/>
///   consuming <see cref="LifeChangedEvent"/> filtered to a NON-controller
///   player (every other player is an opponent, CR 102.2) AND a
///   strictly-positive delta (life *gain*, not loss). The "that many" amount
///   (CR 603.7 — snapshot when the trigger queues) is captured by an
///   <see cref="IEventBus"/> subscription that records the most recent
///   opponent <c>NewLife - PreviousLife</c> delta into a closure-shared mutable
///   slot. The trigger Effect reads + clears the slot on resolution and places
///   that many <see cref="CounterType.PlusOnePlusOne"/> counters on this
///   creature via <see cref="CountersService.Add"/> (so Hardened Scales /
///   Doubling-Season replacements observe the placement, CR 614).
///
/// ## Lifecycle
/// - The closure slot is stamped by the event bus the moment a qualifying
///   opponent <see cref="LifeChangedEvent"/> fires — before TriggerManager
///   evaluates + queues the trigger. By the time the trigger resolves
///   (CR 608) the slot still carries the queued amount, which is then cleared
///   so a stale value can't replay.
///
/// ## Deferred (v1 gaps)
/// - <b>Shape-only path</b>: without an <see cref="IEventBus"/> wiring the
///   closure slot is never stamped, so the counter clause no-ops on
///   hand-executed Effects. Tests that want to assert the counter shape either
///   wire a bus or use the <see cref="SetPendingGainAmount(Creature, int)"/>
///   test hook (the engine wire-up site always passes a bus).
/// </summary>
[CardName("Kavu Predator")]
public static class KavuPredatorFactory
{
    public const string CardName = "Kavu Predator";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 2;
    public const int Toughness = 2;

    // Identity-keyed slot for the "that many" amount snapshot. Stamped by the
    // event-bus subscription (or via the test hook) and consumed + cleared by
    // the trigger Effect at resolution (same posture as Vito).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Creature, AmountSlot>
        _pendingAmounts = new();

    private sealed class AmountSlot
    {
        public int Amount;
    }

    /// <summary>
    /// Construct Kavu Predator with no live runtime services. The lifegain-punish
    /// trigger is attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>); the counter clause no-ops without an event
    /// bus to stamp the amount. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Kavu Predator. When <paramref name="eventBus"/> is supplied,
    /// Kavu Predator subscribes to <see cref="LifeChangedEvent"/> so the "that
    /// many" amount slot is stamped before the trigger resolves. When
    /// <paramref name="triggers"/> is supplied the trigger is registered for
    /// bus-driven firing. When <paramref name="replacements"/> is supplied the
    /// counter placement is routed so CR 614 replacements can rewrite the count.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Kavu });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trample — CR 702.19.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // Pre-allocate the amount slot so SetPendingGainAmount + the event-bus
        // subscription share one identity-keyed cell.
        var slot = new AmountSlot { Amount = 0 };
        _pendingAmounts.AddOrUpdate(card, slot);

        // ----------------------------------------------------------------
        // Event-bus subscription — stamp the "that many" amount BEFORE the
        // trigger queues / resolves. Filtered to OPPONENT-scoped gains
        // (CR 109.5 — a player other than the controller) with a strictly-
        // positive delta (CR 603.7 — snapshotted when the trigger fires).
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            eventBus.Subscribe<LifeChangedEvent>(e =>
            {
                var controller = card.Controller ?? owner;
                if (ReferenceEquals(e.Player, controller)) return;
                var delta = e.NewLife - e.PreviousLife;
                if (delta <= 0) return;
                slot.Amount = delta;
            });
        }

        // ----------------------------------------------------------------
        // Lifegain-punish trigger — CR 603.6a / 119.3 / 109.5 / 603.7.
        //   "Whenever an opponent gains life, put that many +1/+1 counters
        //    on this creature."
        // Triggers.OnLifeGainedByOpponent filters LifeChangedEvent to a
        // non-controller player AND NewLife > PreviousLife. Resolution: place
        // slot.Amount +1/+1 counters on this creature, then reset the slot so a
        // stale value can't replay on a later trigger fired by a different path.
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put that many +1/+1 counters on this creature",
            () =>
            {
                var amount = slot.Amount;
                slot.Amount = 0;
                if (amount <= 0) return;
                CountersService.Add(card, CounterType.PlusOnePlusOne, amount, replacements, eventBus);
            });

        var punishTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLifeGainedByOpponent(owner),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(punishTrigger);
        triggers?.RegisterTriggeredAbility(punishTrigger);

        return card;
    }

    /// <summary>
    /// Test hook — stamp the pending "that many" amount on
    /// <paramref name="kavu"/> directly. Shape-only tests use this to assert the
    /// counter body without wiring an <see cref="IEventBus"/>.
    /// </summary>
    public static void SetPendingGainAmount(Creature kavu, int amount)
    {
        ArgumentNullException.ThrowIfNull(kavu);
        if (_pendingAmounts.TryGetValue(kavu, out var slot))
        {
            slot.Amount = amount;
        }
    }
}
