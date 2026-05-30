using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spellgorger Weird (Guilds of Ravnica, {2}{R}).
///
/// Creature — Weird 2/2. Oracle text (Scryfall, verified):
///   "Whenever you cast a noncreature spell, put a +1/+1 counter on this
///    creature."
///
/// ## Implementation
///
/// - 2/2 Weird, mana cost {2}{R}.
/// - <b>Noncreature-cast counter trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   matches when the spell's controller is Spellgorger Weird's controller
///   (CR 109.5 — "you cast") AND the spell's card does NOT have type
///   <see cref="CardType.Creature"/> (a "noncreature spell" — CR 112.1).
///   Same SpellCastEvent + noncreature predicate as
///   <see cref="MonasteryMentorFactory"/>'s token trigger; the +1/+1-counter
///   payoff mirrors <see cref="PatchworkAutomatonFactory"/> /
///   <see cref="ExperimentOneFactory"/>. On resolution it places one
///   <see cref="CounterType.PlusOnePlusOne"/> counter on the Weird via
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling Season
///   replacements can rewrite the count (CR 614). Active only while on the
///   battlefield.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The cast trigger is
///   attached to the card for inspection but not registered (no live trigger
///   manager). Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ReplacementBus?)"/>
///   — fully wired. Cast trigger registered with <paramref name="triggers"/>;
///   counter placement routes through <see cref="CountersService.Add"/> with
///   the supplied replacement bus (null → direct add).
///
/// ## Deferred (v1 gaps)
/// - None at this layer — the trigger reuses the existing SpellCastEvent
///   plumbing and the counter is a standard CountersService placement.
/// </summary>
[CardName("Spellgorger Weird")]
public static class SpellgorgerWeirdFactory
{
    public const string CardName = "Spellgorger Weird";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Spellgorger Weird with no live wiring. The cast trigger is
    /// attached to the card shape; not registered. Suitable for dispatcher /
    /// shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Spellgorger Weird with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers; not
    /// consumed directly today.</param>
    /// <param name="triggers">TriggerManager for the cast trigger. May be
    /// null — the trigger is still attached to the card shape for
    /// inspection.</param>
    /// <param name="replacements">ReplacementBus for routing the +1/+1
    /// counter placement through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season replacements can rewrite the count
    /// (CR 614). May be null — the counter is placed directly.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Weird });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1.
        //   "Whenever you cast a noncreature spell, put a +1/+1 counter on
        //    this creature."
        // "You cast" → spell.Controller == weird.Controller (CR 109.5).
        // Noncreature gate: !spell.Card.HasType(Creature) (CR 112.1).
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 109.5 — "you cast" matches the source's current controller
            // (owner fallback if control hasn't been resolved yet).
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController)) return false;
            return !e.Spell.Card.HasType(CardType.Creature);
        });

        var counterEffect = new Effect(
            $"{CardName}: +1/+1 counter (whenever you cast a noncreature spell)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                // CR 122.1c — counter placement; routed through
                // CountersService so Hardened Scales / Doubling Season
                // replacements observe the intent (CR 614).
                CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements);
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
