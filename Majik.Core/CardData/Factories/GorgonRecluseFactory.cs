using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gorgon Recluse (Time Spiral, {3}{B}{B}).
///
/// Creature — Gorgon 2/4. Oracle text (verified against the embedded Modern
/// seed / Scryfall 2026-06-10):
///   "Whenever this creature blocks or becomes blocked by a nonblack creature,
///    destroy that creature at end of combat.
///    Madness {B}{B}"
///
/// The base shape (name, Gorgon subtype, {3}{B}{B}, 2/4) is materialised from
/// the embedded JSON definition (<c>gorgon-recluse.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The combat trigger is layered on
/// here (the JSON <c>AbilityDefinition</c> schema doesn't express
/// blocks/blocked-by combat triggers or delayed destroys).
///
/// <b>Madness {B}{B} (CR 702.35)</b> is intrinsic — the central discard funnel
/// <see cref="Fx.DiscardCard"/> consults <c>MadnessCatalog</c> by name and
/// routes a discarded madness card to exile + offers it for its madness cost.
/// No factory code is needed for it and none is added here.
///
/// ## Implemented (v1)
/// - <b>2/4 Creature — Gorgon</b>, mana cost {3}{B}{B}.
/// - <b>Combat trigger (CR 509.1 — block declaration; CR 603.2)</b>:
///   "Whenever this creature blocks or becomes blocked by a nonblack creature,
///   destroy that creature at end of combat." Each blocker→attacker pairing
///   fires one <see cref="CreatureBlocksEvent"/> (carrying both creatures), so
///   the condition matches when Gorgon Recluse is EITHER the
///   <see cref="CreatureBlocksEvent.Blocker"/> (it blocks) OR the
///   <see cref="CreatureBlocksEvent.BlockedAttacker"/> (it becomes blocked by)
///   — and "that creature" (the OTHER creature in the pairing) is nonblack
///   (CR 105.2 / CR 202.3, colour from mana cost + colour indicator via
///   <see cref="CardColors"/>). A black creature in the pairing never triggers.
/// - <b>"destroy that creature at end of combat"</b> is a delayed effect
///   (CR 603.7). When the trigger resolves it does NOT destroy immediately;
///   instead it subscribes a one-shot handler to the controller's
///   <see cref="StepStateType.EndOfCombat"/> <see cref="StepStartedEvent"/>
///   that destroys the captured creature then. A resolution-time legality
///   re-check (CR 608.2b) makes a creature that already left the battlefield a
///   clean no-op. Destroy goes through <see cref="Fx.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> — a plain destroy (no
///   "can't be regenerated" rider), so indestructible (CR 702.12) and an
///   active regeneration shield (CR 701.15) both still apply. Same one-shot
///   end-of-combat subscribe/unsubscribe shape as
///   <see cref="AvatarRokuFactory.AttachFirebending"/>.
///
/// ## No-bus fallback
/// When no <see cref="IEventBus"/> is supplied (shape / dispatcher tests) the
/// trigger still attaches and resolves, but the delayed destroy can't be
/// scheduled (nothing drives the end-of-combat step) — a clean no-op. A live
/// game always supplies the bus.
/// </summary>
[CardName("Gorgon Recluse")]
public static class GorgonRecluseFactory
{
    public const string CardName = "Gorgon Recluse";
    public const string Slug = "gorgon-recluse";

    /// <summary>
    /// Construct Gorgon Recluse owned and controlled by <paramref name="owner"/>
    /// with no live runtime wiring (the <see cref="NamedCardFactory"/> dispatch
    /// target). The combat trigger is attached structurally; without an event
    /// bus the delayed end-of-combat destroy is not scheduled.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Gorgon Recluse with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the resolved combat trigger
    /// subscribes a one-shot <see cref="StepStateType.EndOfCombat"/> handler
    /// that performs the delayed destroy (CR 603.7).</param>
    /// <param name="triggers">When supplied, registers the combat trigger so a
    /// matching <see cref="CreatureBlocksEvent"/> lands it on the stack
    /// automatically.</param>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Gorgon
        // subtype, {3}{B}{B}, 2/4). The JSON carries no abilities — the combat
        // trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 509.1 / CR 603.2 — "Whenever this creature blocks or becomes
        // blocked by a nonblack creature, destroy that creature at end of
        // combat." One CreatureBlocksEvent fires per blocker→attacker pairing
        // (carrying both creatures). The condition matches when Gorgon Recluse
        // is EITHER the blocker (it blocks) OR the blocked attacker (it becomes
        // blocked by), and "that creature" (the OTHER creature) is nonblack.
        // The captured "that creature" is stashed so the resolved effect knows
        // which creature to destroy (mirrors Brimaz's captured-event pattern).
        // ----------------------------------------------------------------
        Creature? capturedThatCreature = null;

        var condition = new EventTriggerCondition<CreatureBlocksEvent>((e, _) =>
        {
            Creature thatCreature;
            if (ReferenceEquals(e.Blocker, card))
                thatCreature = e.BlockedAttacker;        // Gorgon Recluse blocks.
            else if (ReferenceEquals(e.BlockedAttacker, card))
                thatCreature = e.Blocker;                // Gorgon Recluse becomes blocked by.
            else
                return false;                            // Not involving this creature.

            // "a nonblack creature" — a black creature in the pairing never
            // triggers the ability (CR 105.2 / CR 202.3).
            if (IsBlack(thatCreature)) return false;

            capturedThatCreature = thatCreature;
            return true;
        });

        var destroyEffect = new Effect(
            $"{CardName}: destroy that creature at end of combat",
            () =>
            {
                var thatCreature = capturedThatCreature;
                if (thatCreature == null) return;

                // CR 603.7 — delayed effect. Don't destroy now; schedule the
                // destroy for the controller's end-of-combat step. Without a
                // bus there's nothing to drive the step — clean no-op.
                if (eventBus == null) return;

                var controller = card.Controller ?? owner;

                Action<StepStartedEvent>? handler = null;
                handler = ev =>
                {
                    if (ev.StepType != StepStateType.EndOfCombat) return;
                    if (!ReferenceEquals(ev.Player, controller)) return;

                    // CR 608.2b — resolution-time legality re-check: only
                    // destroy if "that creature" is still on the battlefield.
                    if (thatCreature.Zone == ZoneType.Battlefield)
                    {
                        // CR 701.7 — plain destroy (no "can't be regenerated"
                        // rider): indestructible (CR 702.12) and an active
                        // regeneration shield (CR 701.15) both still apply.
                        Fx.MoveToGraveyard(thatCreature, ZoneMoveReason.Destroy);
                    }

                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { destroyEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 105.2 / CR 202.3 — a card is black iff black is among its colors
    /// (mana-cost pips + colour indicator, surfaced by
    /// <see cref="CardColors.GetColors"/>).
    /// </summary>
    private static bool IsBlack(ICard card) =>
        CardColors.GetColors(card).Contains(ManaColor.Black);
}
