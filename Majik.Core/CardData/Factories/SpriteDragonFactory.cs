using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sprite Dragon (Ikoria: Lair of Behemoths, {U}{R}).
///
/// Creature — Faerie Dragon 1/1. Oracle text:
///   "Flying.
///    Whenever you cast a noncreature spell, put a +1/+1 counter on
///    Sprite Dragon."
///
/// ## Implemented (v1)
///
/// - <b>1/1 Creature — Faerie Dragon at {U}{R}</b>. Introduces
///   <see cref="CardSubtype.Faerie"/> (Dragon already present, CR 205.3m).
/// - <b>Flying</b> — wired as a <see cref="KeywordAbility"/> marker so
///   combat code (block-restriction at CR 509.1b) reads it the same way it
///   reads every other printed Flying creature (mirrors
///   <see cref="LedgerShredderFactory"/> / <see cref="PsychicFrogFactory"/>).
/// - <b>Cast-noncreature-spell trigger (CR 603.1)</b> — fires on a
///   <see cref="SpellCastEvent"/> whose <see cref="Majik.Core.Spells.ISpell.Controller"/>
///   matches Sprite Dragon's controller AND whose
///   <see cref="Majik.Core.Spells.ISpell.Card"/> does NOT carry
///   <see cref="CardType.Creature"/> — same predicate shape as
///   <see cref="Majik.Core.Keywords.ProwessFactory"/>, but the effect
///   places a <see cref="CounterType.PlusOnePlusOne"/> counter on Sprite
///   Dragon (CR 122.1) instead of registering a one-turn pump. Persistent
///   accumulator across turns (no per-turn cap, unlike Ledger Shredder's
///   second-spell rider).
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring (no <see cref="TriggerManager"/> registration) and produces the
/// correct card shape for factory-shape / dispatch tests. The trigger is
/// attached to the card but not registered with a
/// <see cref="TriggerManager"/>; callers may invoke the effect directly in
/// tests via <c>trigger.Effects[0].Execute()</c>, or use the
/// <see cref="Create(Player, TriggerManager?)"/> overload for bus-driven
/// firing.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Continuous P/T recomputation</b> — Sprite Dragon's effective P/T
///   is derived from base 1/1 plus +1/+1 counters via the standard
///   <see cref="CounterCollection"/> path (CR 613.4 layer 7d), inherited
///   from every other +1/+1-counter user (Psychic Frog activated ability,
///   Ledger Shredder surveil rider, Undying-return creatures). No
///   Sprite-Dragon-specific layer wiring required.
/// </summary>
[CardName("Sprite Dragon")]
public static class SpriteDragonFactory
{
    public const string CardName = "Sprite Dragon";
    public const string Cost = "{U}{R}";

    /// <summary>
    /// Constructs Sprite Dragon with no live <see cref="TriggerManager"/>
    /// wiring. The cast-noncreature trigger is attached to the card for
    /// shape; it is NOT registered. Suitable for factory-shape / dispatch
    /// tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Constructs Sprite Dragon. When <paramref name="triggers"/> is
    /// supplied, the cast-noncreature trigger is registered so a
    /// <see cref="SpellCastEvent"/> from a noncreature spell cast by
    /// Sprite Dragon's controller automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Dragon });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.9 — Flying. KeywordAbility marker; combat code reads it.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Cast-noncreature-spell trigger — CR 603.1 / 122.1.
        //   "Whenever you cast a noncreature spell, put a +1/+1 counter
        //    on Sprite Dragon."
        // Predicate shape mirrors ProwessFactory: controller match AND the
        // spell's card lacks the Creature type. Effect drops a single
        // +1/+1 counter on Sprite Dragon (CR 122.1c — counters are placed
        // directly, no SBA gating).
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it (cast noncreature spell)",
            () => card.Counters.Add(CounterType.PlusOnePlusOne));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
                ReferenceEquals(e.Spell.Controller, owner)
                && !e.Spell.Card.HasType(CardType.Creature)),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
