using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bonecrusher Giant // Stomp (Throne of Eldraine,
/// {2}{R}).
///
/// ## Card text
/// - Bonecrusher Giant — Creature — Giant {2}{R}, 4/3.
///     "Whenever Bonecrusher Giant becomes the target of a spell,
///      Bonecrusher Giant deals 2 damage to that spell's controller."
/// - Stomp (Adventure) — Instant — Adventure {1}{R}.
///     "Damage can't be prevented this turn. Stomp deals 2 damage to any
///      target."
///
/// ## Implemented (v1)
/// - 4/3 Giant creature with mana cost {2}{R}.
/// - Targeted-by-spell trigger (CR 603.6c, 115.6) wired via
///   <see cref="TargetsChosenEvent"/>. Predicate fires when a spell on the
///   stack picks this Bonecrusher Giant as one of its chosen targets and
///   the spell is controlled by someone other than nobody (deals 2 damage
///   to that spell's controller — same player may target themselves and
///   take the damage).
/// - On resolution: publishes a <see cref="DamageDealtEvent"/>
///   (DamageType.Ability — the damage is dealt by the triggered ability,
///   not by the spell on the stack, per CR 119.2c) and calls
///   <see cref="Player.LoseLife"/> on the spell's controller. We use
///   <c>LoseLife</c> because the engine has no central
///   "deal damage to a player" routine outside combat; spell/ability
///   damage to a player is life loss for SBA + frontend purposes (CR
///   120.3 — damage dealt to a player causes that player to lose that
///   much life).
///
/// ## Deferred (v1 gaps)
/// - <b>Adventure cast-from-exile (CR 715)</b>: the Stomp half is not
///   shipped. Adventures require:
///     1. A split-card / dual-faced data model where casting the Adventure
///        face exiles the card if it resolves instead of going to the
///        graveyard (CR 715.2),
///     2. An alternative-cost / cast-from-exile rule that lets the owner
///        cast Bonecrusher Giant from exile until it leaves exile (CR
///        715.3),
///     3. A "damage can't be prevented this turn" replacement-effect
///        global flag.
///   `Majik.Core/CardData/Adventures/` has an `AdventureState.cs` stub but
///   no cast pipeline yet — once that pipeline exists, the Stomp instant
///   side can be added without disturbing this factory.
/// - <b>Live wiring against <see cref="Majik.Core.Services.SpellCaster"/></b>:
///   the trigger registers with a passed-in <see cref="TriggerManager"/>
///   when the live overload is used. Production cast paths publish
///   <see cref="TargetsChosenEvent"/> from <see cref="Majik.Core.Services.SpellCaster"/>,
///   so the trigger surfaces as pending automatically. The single-arg
///   factory attaches the ability for shape tests; the trigger only fires
///   when an event bus is involved.
/// </summary>
public static class BonecrusherGiantFactory
{
    /// <summary>
    /// Construct Bonecrusher Giant with no live event-bus / trigger-manager
    /// wiring. The targeted-by-spell trigger is attached to the card so
    /// structural / dispatch tests see the ability shape, but it is not
    /// registered with a <see cref="TriggerManager"/>; tests fire it
    /// manually via <see cref="TriggeredAbility.IsTriggered"/> or by
    /// executing the effect directly.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Bonecrusher Giant with optional event bus + trigger
    /// manager. When <paramref name="triggers"/> is supplied, the
    /// targeted-by-spell trigger is registered so a
    /// <see cref="TargetsChosenEvent"/> matching this Bonecrusher Giant
    /// automatically surfaces as pending.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Bonecrusher Giant",
            manaCost: "{2}{R}",
            power: 4,
            toughness: 3,
            subtypes: new[] { CardSubtype.Giant });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Targeted-by-spell trigger — CR 603.6c, 115.6.
        //   "Whenever Bonecrusher Giant becomes the target of a spell,
        //    Bonecrusher Giant deals 2 damage to that spell's controller."
        //
        // Fires on TargetsChosenEvent where:
        //   - the stack object is a spell (Spells.ISpell), AND
        //   - one of the chosen targets references this Bonecrusher Giant
        //     (TargetType.Permanent or TargetType.Card, since spells may
        //     target either).
        //
        // The trigger resolves by dealing 2 damage to the spell's
        // controller — which we model as a DamageDealtEvent + LoseLife,
        // matching the pattern used by other non-combat ping cards.
        // ----------------------------------------------------------------

        // Capture spell controller at trigger-evaluation time. We need the
        // *spell's* controller (not the target's) so that targeting your
        // own Bonecrusher with your own spell deals 2 to you.
        Player? capturedSpellController = null;

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            // Only spells trigger this — not activated/triggered abilities
            // that target. CR 115.6 specifies "becomes the target of a
            // spell".
            if (e.StackObject is not Majik.Core.Spells.ISpell spell)
            {
                return false;
            }

            // Is this Bonecrusher Giant in the chosen-target list?
            var matched = e.Targets.Any(t =>
                (t.TargetType == TargetType.Permanent || t.TargetType == TargetType.Card)
                && t is Target concrete
                && ReferenceEquals(concrete.TargetObject, card));

            if (!matched)
            {
                return false;
            }

            capturedSpellController = spell.Controller;
            return true;
        });

        var pingEffect = new Effect(
            "Bonecrusher Giant: deal 2 damage to that spell's controller",
            () =>
            {
                var target = capturedSpellController;
                if (target == null)
                {
                    return;
                }

                // CR 119.2c — non-combat damage from a triggered ability.
                eventBus?.Publish(new DamageDealtEvent(
                    sourceCard: card,
                    sourcePlayer: null,
                    targetCard: null,
                    targetPlayer: target,
                    amount: 2,
                    damageType: DamageType.Ability));

                // CR 120.3 — damage dealt to a player causes that player to
                // lose that much life. We bypass any prevention rider
                // (Bonecrusher Giant's Adventure half says "damage can't be
                // prevented", but the creature side itself has no
                // prevention rider — prevention infra is also deferred, so
                // damage goes through unconditionally for now).
                target.LoseLife(2);

                // Clear the captured reference so a future fire doesn't
                // accidentally reuse stale state.
                capturedSpellController = null;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { pingEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        // Live registration with TriggerManager so the bus actually
        // surfaces the trigger as pending when a spell targets this card.
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
