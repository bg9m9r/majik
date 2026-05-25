using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eidolon of the Great Revel (Journey into Nyx,
/// {R}{R}).
///
/// Creature — Spirit 2/2. Oracle text:
///   "Whenever a player casts a spell with mana value 3 or less, Eidolon
///    of the Great Revel deals 2 damage to that player."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Spirit, mana cost {R}{R}.
/// - <b>Cheap-spell triggered ability (CR 603.1)</b> over
///   <see cref="SpellCastEvent"/>:
///     * Fires for ANY player's spell (controller's own included — CR 700.6
///       "a player" is unrestricted).
///     * The spell's mana value (CR 202.3) is &lt;= 3. v1 reads the printed
///       <see cref="Majik.Core.Cards.Card.ManaCostValue"/> via
///       <see cref="Majik.Core.ValueObjects.ManaCost.TotalValue"/>; X-spells
///       use the chosen-X value if it has been stamped onto the card by the
///       cast flow (CR 202.3b), otherwise X = 0 (matches Eidolon's actual
///       resolved-on-stack mana-value reading).
/// - <b>Resolve</b>: 2 damage (CR 119) to the player who cast the
///   triggering spell. Routed through <see cref="Fx.DealDamage"/> so
///   <see cref="Player.LifeLostThisTurn"/> increments and downstream
///   spectacle / revolt / lifegain triggers observe the loss.
/// - <b>Triggers on Eidolon's OWN cast</b>: oracle "a player casts a spell
///   with mana value 3 or less" is unconditional; if Eidolon were ever
///   shipped with a printed cost MV &lt;= 3 it would self-trigger. The
///   factory ships printed cost {R}{R} (MV 2) so this is currently a
///   non-issue, but the predicate is intentionally MV-only (no name
///   exclusion) to preserve the rule shape.
///
/// ## Deferred (v1 gaps)
/// - <b>Mana value of split / adventure cards</b>: v1 reads
///   <see cref="Majik.Core.Cards.Card.ManaCostValue"/> directly. Cast-side
///   modal / split mana value calculations are not yet pushed onto the
///   stack object itself (CR 712 / CR 722 — split / fuse spells), so a
///   spell cast for an adventure cost reports the printed mana cost rather
///   than the resolved face's cost. Eidolon is intentionally generous —
///   "mana value of the spell" per CR 202.3a reads the printed cost when
///   the spell is on the stack with no chosen mode, which matches v1.
/// - <b>Cost-reduction effects</b> (e.g. Goblin Electromancer) do NOT
///   change mana value (CR 202.3b — "alternative costs, additional costs,
///   cost reductions don't change mana value"). v1 is already correct
///   here because we read the printed cost, not what was paid.
/// </summary>
[CardName("Eidolon of the Great Revel")]
public static class EidolonOfTheGreatRevelFactory
{
    public const string CardName = "Eidolon of the Great Revel";
    public const string PrintedManaCost = "{R}{R}";

    /// <summary>
    /// Construct Eidolon with no live TriggerManager wiring. The trigger
    /// is attached to the card shape so dispatcher tests see it; pass the
    /// (owner, triggers) overload to register it for live SpellCastEvent
    /// dispatch.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Eidolon of the Great Revel with optional TriggerManager
    /// wiring. When <paramref name="triggers"/> is supplied, the trigger
    /// is registered so any <see cref="SpellCastEvent"/> for a spell with
    /// mana value 3 or less automatically queues 2-damage-to-caster on
    /// the stack.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // The caster of the triggering spell — captured by the predicate
        // for the resolve body. CR 603.3 — trigger condition is evaluated
        // before the ability goes on the stack, so the captured caster is
        // fresh by the time the effect resolves. Boxed in a single-element
        // array so the closure can rebind it.
        var pendingCaster = new Player?[] { null };

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var caster = e.Spell.Controller;
            if (caster is null) return false;

            // CR 202.3 — mana value of the spell. Reads the printed mana
            // cost on the card. X-spells: if the cast flow stamped the
            // chosen X via SetPendingCastX, fold it in; otherwise X = 0.
            var spellCard = e.Spell.Card;
            int mv = 0;
            if (spellCard is Card concrete)
            {
                mv = concrete.ManaCostValue.TotalValue;
                if (concrete.PendingCastX is int x) mv += x;
            }

            if (mv > 3) return false;

            pendingCaster[0] = caster;
            return true;
        });

        var damageEffect = new Effect(
            $"{CardName}: 2 damage to the spell's caster",
            () =>
            {
                var caster = pendingCaster[0];
                pendingCaster[0] = null;
                if (caster is null) return;

                // CR 119 — direct damage to a player. Player → Fx.DealDamage
                // routes to Player.LoseLife, which increments
                // LifeLostThisTurn (so Spectacle / Revolt / lifegain
                // observers see the loss).
                Fx.DealDamage(caster, 2);
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
