using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ash Zealot (Return to Ravnica, {R}{R}).
///
/// Creature — Human Warrior 2/2. Oracle text:
///   "First strike, haste
///    Whenever a player casts a spell from a graveyard, this creature deals
///    3 damage to that player."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Warrior, mana cost {R}{R}.
/// - <b>First strike (CR 702.7) + Haste (CR 702.10)</b> as marker
///   <see cref="KeywordAbility"/>s (read by CombatAbilities + the
///   summoning-sickness gate).
/// - <b>Graveyard-cast punisher trigger (CR 603.1)</b> over
///   <see cref="SpellCastEvent"/>:
///     * Fires for ANY player's spell (the controller's own cast included —
///       CR 700.6 "a player" is unrestricted, no controller exclusion).
///     * Gated on the spell being cast from a graveyard
///       (<see cref="Majik.Core.Spells.Spell.WasCastFromGraveyard"/>, stamped
///       by <see cref="Majik.Core.Game.SpellCastFlow"/> on a Graveyard → Stack
///       cast). Flashback / Escape / Disturb and any "you may cast this from
///       your graveyard" permission all qualify (CR 702.34 / 702.138 /
///       702.143).
/// - <b>Resolve</b>: 3 damage (CR 119) to the player who cast the triggering
///   spell — "that player". Routed through <see cref="Fx.DealDamage"/> so
///   <see cref="Player.LifeLostThisTurn"/> increments (downstream Spectacle /
///   Revolt / lifegain observers see the loss).
///
/// ## Notes
/// - The "deal N damage to the triggering player" shape uses the
///   established boxed-closure capture idiom (the trigger predicate stores the
///   caster; the resolve body reads + clears it) — the SAME pattern
///   <see cref="EidolonOfTheGreatRevelFactory"/> uses. CR 603.3 — the trigger
///   condition is evaluated before the ability goes on the stack, so the
///   captured caster is the right "that player" by resolution time.
/// </summary>
[CardName("Ash Zealot")]
public static class AshZealotFactory
{
    public const string CardName = "Ash Zealot";
    public const string PrintedManaCost = "{R}{R}";

    /// <summary>
    /// Construct Ash Zealot with no live TriggerManager wiring. The trigger is
    /// attached to the card shape so dispatcher tests see it; pass the
    /// (owner, triggers) overload to register it for live SpellCastEvent
    /// dispatch.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Ash Zealot with optional TriggerManager wiring. When
    /// <paramref name="triggers"/> is supplied, the graveyard-cast trigger is
    /// registered so any <see cref="SpellCastEvent"/> for a spell cast from a
    /// graveyard automatically queues 3-damage-to-caster on the stack.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.7 — First strike. CR 702.10 — Haste. Marker keywords.
        card.AddAbility(new KeywordAbility("First strike", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // The caster of the triggering graveyard spell — captured by the
        // predicate for the resolve body. CR 603.3 — the trigger condition is
        // evaluated before the ability goes on the stack, so the captured
        // caster is fresh by the time the effect resolves. Boxed in a
        // single-element array so the closure can rebind it.
        var pendingCaster = new Player?[] { null };

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 113.5 / 601.2 — only spells cast from a graveyard punish.
            if (e.Spell is not Majik.Core.Spells.Spell s || !s.WasCastFromGraveyard)
            {
                return false;
            }

            var caster = e.Spell.Controller;
            if (caster is null) return false;

            pendingCaster[0] = caster;
            return true;
        });

        var damageEffect = new Effect(
            $"{CardName}: 3 damage to the graveyard-spell's caster",
            () =>
            {
                var caster = pendingCaster[0];
                pendingCaster[0] = null;
                if (caster is null) return;

                // CR 119 — direct damage to a player. Player → Fx.DealDamage
                // routes to Player.LoseLife, which increments LifeLostThisTurn.
                Fx.DealDamage(caster, 3);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
