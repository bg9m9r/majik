using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.40 — Storm. "Storm is a triggered ability that functions on the
/// stack. 'Storm' means 'When you cast this spell, copy it for each other
/// spell that was cast before it this turn. If the spell has any targets,
/// you may choose new targets for any of the copies.'"
///
/// Helper builds the on-cast triggered ability for storm-bearing spells
/// (Brain Freeze, Mind's Desire, Tendrils of Agony, etc.). The trigger
/// listens for a <see cref="SpellCastEvent"/> on the source card, counts
/// the controller's spells cast this turn (minus this spell itself) via
/// <see cref="TurnState.SpellsCastByPlayer"/>, and pushes that many copies
/// of the spell via <see cref="SpellCopier.PushCopyOfTopSpell"/>.
///
/// ## Implemented (v1)
/// - Pure on-cast triggered ability over <see cref="SpellCastEvent"/>,
///   gated to the source card. Same shape as
///   <see cref="Majik.Core.CardData.Factories.CrashingFootfallsFactory"/>'s
///   cascade trigger.
/// - Count is read at <b>condition-evaluation time</b> from
///   <see cref="TurnState.SpellsCastByPlayer"/> and captured into a closure
///   so the resolve-time effect uses a snapshot. <see cref="TurnDriver"/>
///   subscribes to <see cref="SpellCastEvent"/> via a typed
///   <see cref="EventBus.Subscribe{T}(Action{T})"/> handler; the
///   <see cref="TriggerManager"/> subscribes via
///   <see cref="EventBus.SubscribeAll"/>. The bus invokes typed handlers
///   before global handlers (see <c>EventBus.Publish</c>), so by the time
///   storm's condition runs the spell-being-cast has already been counted
///   into <see cref="TurnState.SpellsCastByPlayer"/>. The helper subtracts
///   1 to recover the "other spells" count CR 702.40a requires.
/// - Copy creation reuses <see cref="SpellCopier.PushCopyOfTopSpell"/>:
///   v1 re-executes the original spell's effect list in place per copy.
///   For Brain Freeze (mill 3 to chosen target player), N copies =
///   <c>3 + 3N</c> total cards milled by the storm count.
///
/// ## Deferred (v1 gaps)
/// - <b>"Choose new targets for any of the copies"</b> (CR 702.40a /
///   CR 707.10a): inherited from <see cref="SpellCopier"/> — the v1 copier
///   reuses the original targets verbatim. Same gap as
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.NextSpellCopyTemplate"/>.
/// - <b>Copies as distinct stack objects</b>: <see cref="SpellCopier"/>
///   re-executes effects in place rather than pushing real
///   <see cref="ISpell"/> stack objects, so anything subscribing to
///   <see cref="Majik.Core.Domain.DomainEvents.StackObjectAddedEvent"/>
///   won't see the copies. Acceptable for the Brain Freeze observable
///   contract ("N copies → N additional mills").
/// - <b>No <see cref="TurnState"/> wired</b>: when the helper is called
///   with a null <see cref="TurnState"/> (test / shape-only path), the
///   storm count falls back to zero and no copies are made — the trigger
///   still fires structurally so shape tests can observe its presence.
/// </summary>
public static class StormHelper
{
    /// <summary>
    /// Build the storm on-cast triggered ability for <paramref name="card"/>.
    /// Caller is responsible for attaching the returned ability to the
    /// card (<see cref="ICard.AddAbility"/>) and optionally registering it
    /// with a <see cref="TriggerManager"/> for bus-driven firing.
    /// </summary>
    /// <param name="card">The storm-bearing spell. Identity for the
    /// <see cref="SpellCastEvent"/> gate.</param>
    /// <param name="controller">The card's controller — also the trigger
    /// controller per CR 113.6.</param>
    /// <param name="stack">Stack used to push copies through
    /// <see cref="SpellCopier.PushCopyOfTopSpell"/>. May be null in shape
    /// tests; the resolve effect no-ops when stack is null.</param>
    /// <param name="turnState">Live per-turn ledger consulted at
    /// condition-evaluation time for the spells-cast-by-controller count.
    /// May be null in shape tests; the storm count then resolves to zero
    /// (no copies created) but the trigger still fires structurally.</param>
    public static TriggeredAbility Build(
        ICard card,
        Player controller,
        Majik.Core.Stack.Stack? stack,
        TurnState? turnState)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(controller);

        // Capture-into-closure: the condition predicate runs the moment the
        // SpellCastEvent for `card` is published, so it sees the live
        // SpellsCastByPlayer count. We snapshot (a) the original spell
        // reference so the resolve-time copy effect can re-execute its
        // effect list, and (b) the storm count (other-spells = total - 1).
        ISpell? capturedSpell = null;
        var capturedStormCount = 0;

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 702.40a — "When you cast this spell" gate.
            if (!ReferenceEquals(e.Spell.Card, card)) return false;

            // CR 702.40a — count OTHER spells cast this turn. TurnDriver's
            // SpellCastEvent subscriber (typed) has already incremented the
            // tally for this spell before our SubscribeAll handler runs
            // (see EventBus.Publish — typed sync handlers fire before
            // global sync handlers). Subtract 1 to recover the "other"
            // count.
            var total = turnState?.SpellsCastByPlayer(controller) ?? 0;
            capturedStormCount = Math.Max(0, total - 1);
            capturedSpell = e.Spell;
            return true;
        });

        var copyEffect = new Effect(
            $"Storm — copy this spell for each other spell cast this turn (CR 702.40)",
            () =>
            {
                if (capturedSpell == null) return;
                if (capturedStormCount <= 0) return;
                if (stack == null) return;

                for (var i = 0; i < capturedStormCount; i++)
                {
                    SpellCopier.PushCopyOfTopSpell(stack, capturedSpell);
                }
            });

        return new TriggeredAbility(
            source: card,
            controller: controller,
            condition: condition,
            effects: new IEffect[] { copyEffect },
            // Storm "functions on the stack" (CR 702.40a) — the spell-cast
            // event publishes after the card moves to the Stack zone.
            activeZones: new[] { ZoneType.Stack });
    }
}
