using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vito, Thorn of the Dusk Rose (Core Set 2021,
/// {1}{B}{B}).
///
/// Legendary Creature — Vampire Knight 1/3. Oracle text:
///   "Lifelink
///    Whenever you gain life, each opponent loses that much life."
///
/// ## Implemented (v1)
/// - 1/3 Legendary Creature — Vampire Knight, mana cost {1}{B}{B}, owner /
///   controller wired.
/// - <b>Lifelink (CR 702.15)</b>: <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasLifelink"/> reads it
///   for the combat-damage lifegain pipeline (same wiring as Atraxa /
///   Daybreak Coronet / Heliod's grant target).
/// - <b>Lifegain triggered ability (CR 603.6a / CR 119.3)</b>:
///   "Whenever you gain life, each opponent loses that much life." Wired
///   via <see cref="Triggers.OnLifeGainedByPlayer"/> consuming
///   <see cref="LifeChangedEvent"/> filtered to the controller AND
///   strictly-positive deltas. The "that much" amount (CR 603.7 —
///   snapshot at trigger queueing) is captured by an
///   <see cref="IEventBus"/> subscription that records the most recent
///   <c>NewLife - PreviousLife</c> delta into a closure-shared mutable
///   slot. The trigger Effect reads + clears the slot on resolution and
///   drains that amount from every opponent returned by the resolver.
///
/// ## Lifecycle
/// - The closure slot is stamped by the event bus the moment
///   <see cref="LifeChangedEvent"/> fires — before TriggerManager
///   evaluates and queues the trigger. By the time the trigger resolves
///   (CR 608 — resolution pops it off the stack), the slot still carries
///   the queued amount.
///
/// ## Deferred (v1 gaps)
/// - <b>Live opponent enumeration without a resolver</b>: same gap as
///   <see cref="SheoldredTheApocalypseFactory"/> and Cliffhaven Vampire.
/// - <b>Shape-only path</b>: without an <see cref="IEventBus"/> wiring,
///   the closure slot is never stamped, so the drain clause no-ops on
///   hand-executed Effects. Tests that want to assert the drain shape
///   either wire a bus or use the
///   <see cref="SetPendingGainAmount(Creature, int)"/> test hook (the
///   engine wire-up site should always pass a bus).
/// </summary>
[CardName("Vito, Thorn of the Dusk Rose")]
public static class VitoThornOfTheDuskRoseFactory
{
    public const string CardName = "Vito, Thorn of the Dusk Rose";
    public const string PrintedManaCost = "{1}{B}{B}";
    public const int Power = 1;
    public const int Toughness = 3;

    // Identity-keyed slot for the "that much" amount snapshot. Stamped by
    // the event-bus subscription (or via the test hook) and consumed +
    // cleared by the trigger Effect at resolution.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Creature, AmountSlot>
        _pendingAmounts = new();

    private sealed class AmountSlot
    {
        public int Amount;
    }

    /// <summary>
    /// Construct Vito with no live runtime services. The lifegain trigger
    /// is attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>) and the drain clause is a no-op
    /// without an opponent resolver / event bus. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Vito, Thorn of the Dusk Rose. When
    /// <paramref name="opponentResolver"/> is supplied, the lifegain trigger
    /// drains "that much" life from every opponent it returns. When
    /// <paramref name="eventBus"/> is supplied, Vito subscribes to
    /// <see cref="LifeChangedEvent"/> so the amount slot is stamped before
    /// the trigger resolves. When <paramref name="triggers"/> is supplied
    /// the trigger is registered for bus-driven firing.
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Lifelink — CR 702.15.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // Pre-allocate the amount slot so SetPendingGainAmount + the
        // event-bus subscription share one identity-keyed cell.
        var slot = new AmountSlot { Amount = 0 };
        _pendingAmounts.AddOrUpdate(card, slot);

        // ----------------------------------------------------------------
        // Event-bus subscription — stamp the "that much" amount BEFORE the
        // trigger queues / resolves. Filtered to controller-scoped gains
        // (CR 603.7 — the value is snapshotted when the trigger fires).
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            eventBus.Subscribe<LifeChangedEvent>(e =>
            {
                if (!ReferenceEquals(e.Player, card.Controller ?? owner)) return;
                var delta = e.NewLife - e.PreviousLife;
                if (delta <= 0) return;
                slot.Amount = delta;
            });
        }

        // ----------------------------------------------------------------
        // Lifegain trigger — CR 603.6a / 119.3 / 603.7.
        //   "Whenever you gain life, each opponent loses that much life."
        // Filter: LifeChangedEvent.Player == controller AND NewLife > Prev.
        // Resolution: drain slot.Amount from each opponent returned by the
        // resolver. The slot is reset to 0 after drain so a stale value
        // can't replay on a later trigger fired by a different (non-life-
        // gain) path.
        // ----------------------------------------------------------------
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses that much life",
            () =>
            {
                var amount = slot.Amount;
                slot.Amount = 0;
                if (amount <= 0) return;

                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;
                foreach (var opp in opponents)
                {
                    if (opp == null) continue;
                    if (ReferenceEquals(opp, owner)) continue;
                    opp.LoseLife(amount);
                }
            });

        var lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLifeGainedByPlayer(owner),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lifegainTrigger);
        triggers?.RegisterTriggeredAbility(lifegainTrigger);

        return card;
    }

    /// <summary>
    /// Test hook — stamp the pending "that much" amount on
    /// <paramref name="vito"/> directly. Shape-only tests use this to
    /// assert the drain body without wiring an <see cref="IEventBus"/>.
    /// </summary>
    public static void SetPendingGainAmount(Creature vito, int amount)
    {
        if (vito == null) throw new ArgumentNullException(nameof(vito));
        if (_pendingAmounts.TryGetValue(vito, out var slot))
        {
            slot.Amount = amount;
        }
    }
}
