using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Chalice of the Void (Mirrodin, {X}{X}).
///
/// Artifact. Oracle text:
///   "Chalice of the Void enters the battlefield with X charge counters
///    on it."
///   "Whenever a player casts a spell with mana value equal to the number
///    of charge counters on Chalice of the Void, counter that spell."
///
/// ## Implemented (v1)
/// - Artifact {X}{X} with owner/controller wired.
/// - <b>ETB trigger</b> (CR 603.6a, CR 122.1g): on entering the
///   battlefield, places <c>X</c> <see cref="CounterType.Charge"/>
///   counters on Chalice. X is read from
///   <see cref="Card.PendingCastX"/>, stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> at cast time right
///   after the caster's <c>ChooseXAsync</c>. The stamp is consumed
///   (cleared) so a later non-cast battlefield entry (blink, copy)
///   doesn't reuse it — such an entry leaves Chalice with zero charge
///   counters, matching the printed behaviour for a Chalice that
///   didn't come in via a real X cast.
/// - <b>Counter-spell triggered ability</b> (CR 603.2, CR 701.5):
///   on every <see cref="SpellCastEvent"/>, if the cast spell's mana
///   value equals the current charge-counter count on Chalice, the
///   trigger queues a counter for that spell. On resolution, the
///   queued spell is removed from the stack (when a
///   <see cref="Majik.Core.Stack.Stack"/> is supplied) and moved to
///   its owner's graveyard.
///   Symmetric — counters BOTH players' spells, including Chalice's
///   controller's own. Chalice's own cast does not trigger itself
///   because Chalice has no charge counters at SpellCastEvent time
///   (it is still on the stack, not on the battlefield).
///
/// ## Notes on mana-value comparison (CR 202.3b)
/// "Mana value of a spell with {X}" is computed as the printed mana
/// value plus the chosen X. The engine collapses {X}{X} into a single
/// generic-X cost, so a Chalice cast for <c>X=2</c> reports MV = 2 in
/// the trigger comparison (matching the engine's overall X handling).
/// This is consistent with how the cost is paid (printed + X via
/// <see cref="Majik.Core.ValueObjects.ManaCost.AddGenericCost"/>) and
/// is what other spell-MV consumers (Up the Beanstalk, Force of
/// Negation) already see.
///
/// ## Deferred (v1 gaps)
/// - <b>Strict 122.1g timing</b>: counters should be placed as Chalice
///   enters (122.1g "with") rather than via an ETB trigger that puts an
///   ability on the stack. The v1 impl uses an ETB-trigger effect for
///   the same reason Murktide Regent does — no general 122.1g
///   replacement-effect surface yet — and the observable end state is
///   identical for the test matrix here.
/// </summary>
[CardName("Chalice of the Void")]
public static class ChaliceOfTheVoidFactory
{
    /// <summary>
    /// Construct Chalice of the Void with no live runtime wiring. Both
    /// triggered abilities are attached to the card shape; neither is
    /// registered with a <see cref="TriggerManager"/>, and the counter
    /// effect falls back to direct graveyard placement (no
    /// <see cref="Majik.Core.Stack.Stack"/> handle). Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, stack: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Chalice of the Void with optional runtime services.
    /// When <paramref name="triggers"/> is supplied both the ETB
    /// counter-placement trigger and the cast-spell counter trigger are
    /// registered; when <paramref name="stack"/> is supplied the
    /// counter effect routes through
    /// <see cref="OracleSpellBinder.RemoveFromStack"/> so the resolver
    /// no longer sees the countered spell.
    /// </summary>
    public static Artifact Create(
        Player owner,
        Majik.Core.Stack.Stack? stack,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact("Chalice of the Void", "{X}{X}");
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a, CR 122.1g.
        //   "Chalice of the Void enters the battlefield with X charge
        //    counters on it."
        // v1 folds 122.1g "as it enters with N counters" into the ETB
        // trigger effect: read PendingCastX (stamped by SpellCastFlow
        // right after ChooseXAsync), apply that many Charge counters,
        // then clear the stamp so re-entries (blink, copy) don't reuse
        // the value. PendingCastX is null for non-cast entries → 0
        // counters, matching the printed text.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Chalice of the Void — enters with X charge counters (CR 122.1g)",
            () =>
            {
                var x = card.PendingCastX ?? 0;
                if (x > 0)
                {
                    card.Counters.Add(CounterType.Charge, x);
                }
                card.ClearPendingCastX();
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Counter-spell trigger — CR 603.2, CR 701.5.
        //   "Whenever a player casts a spell with mana value equal to
        //    the number of charge counters on Chalice of the Void,
        //    counter that spell."
        // Symmetric: fires on ANY player's cast, including the
        // controller's own. The condition predicate snapshots each
        // matching spell into a per-card queue so the effect (which
        // runs later when the trigger resolves) knows which spell to
        // counter. Multiple stacked Chalices each get their own queue
        // — Chalice instances are independent factory outputs.
        // ----------------------------------------------------------------
        var pendingCounters = new Queue<ISpell>();

        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 202.3b — printed mv + chosen X. PendingCastX is still
            // set on the card at SpellCastEvent time (the card is on the
            // stack, hasn't resolved → no ETB has consumed it yet).
            var castCard = e.Spell.Card;
            var printed = castCard is Card concrete
                ? concrete.ManaCostValue.TotalValue
                : Majik.Core.ValueObjects.ManaCost.Parse(castCard.ManaCost).TotalValue;
            var x = (castCard as Card)?.PendingCastX ?? 0;
            var manaValue = printed + x;

            if (manaValue != card.Counters.Count(CounterType.Charge))
            {
                return false;
            }

            pendingCounters.Enqueue(e.Spell);
            return true;
        });

        var counterEffect = new Effect(
            "Chalice of the Void — counter the triggering spell (CR 701.5)",
            () =>
            {
                if (pendingCounters.Count == 0) return;
                var spell = pendingCounters.Dequeue();

                // CR 701.5 — counter: remove from stack, then the card
                // is put into its owner's graveyard. CR 608.2b: if the
                // spell is already off the stack by the time we resolve
                // (e.g. another effect countered it), the stack
                // walk is a no-op and we still ensure the card ends
                // up in its owner's graveyard.
                if (stack != null)
                {
                    OracleSpellBinder.RemoveFromStack(stack, spell);
                }
                if (spell.Card.Owner != null
                    && spell.Card.Zone != ZoneType.Graveyard)
                {
                    spell.Card.Owner.Zones.Graveyard.AddCard(spell.Card);
                }
                spell.Card.SetZone(ZoneType.Graveyard);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
