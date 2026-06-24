using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Razzle-Dazzler (Bloomburrow, {1}{U}).
///
/// Creature — Human Wizard 1/2. Oracle text (verified against Scryfall):
///   "Whenever you cast your second spell each turn, put a +1/+1 counter on
///    this creature. It can't be blocked this turn."
///
/// ## Implemented (v1)
/// - 1/2 Human Wizard built from the embedded JSON shape
///   (<c>razzle-dazzler.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - One triggered ability wired through the trigger pipeline: a SpellCast
///   watcher that fires on the controller's 2nd cast each turn (CR 603.2).
///   This is the same "cast your second spell each turn" trigger as
///   <see cref="LedgerShredderFactory"/>; the per-turn count is held in a
///   closure private to this card instance and reset by a
///   <see cref="TurnStartedEvent"/> subscription when an event bus is
///   supplied (CR 500.1).
/// - On trigger resolution the effect (CR 608.2e — one resolution clause):
///     1. Puts a +1/+1 counter on this creature (CR 122.1), routed through
///        <see cref="CountersService.Add"/> so Hardened Scales / Doubling
///        Season replacements can rewrite the count (CR 614).
///     2. Registers a "can't be blocked this turn" combat restriction
///        (CR 509.1c) on this creature's own
///        <see cref="Permanent.ActiveEffects"/> as a
///        <see cref="CombatRestrictionEffect"/>(<see cref="CombatRestriction.CannotBeBlocked"/>,
///        <c>expiresAtEndOfTurn: true</c>) — the "this turn" rider (CR 514.2).
///        Mirrors the can't-be-blocked half of
///        <see cref="DistortionStrikeFactory"/>, but scoped to the source
///        creature itself rather than a chosen target.
///
/// ## Deferred (v1 gaps)
/// - Cast-counting predicate increments on every <see cref="SpellCastEvent"/>
///   for the controller — including Razzle-Dazzler's own cast (a creature
///   spell counts as a spell, per CR 700.2). That's correct for the
///   second-spell-each-turn rider but means callers exercising the trigger
///   manually must publish a SpellCastEvent for the first spell too. Tests
///   below do this. Same posture as <see cref="LedgerShredderFactory"/>.
/// </summary>
[CardName("Razzle-Dazzler")]
public static class RazzleDazzlerFactory
{
    public const string Slug = "razzle-dazzler";

    /// <summary>
    /// Construct Razzle-Dazzler with no live bus / trigger-manager wiring.
    /// The triggered ability is attached to the card so structural tests can
    /// observe its shape; the per-turn count is held in a closure but is
    /// never reset (callers exercising the trigger manually can reset by
    /// constructing a fresh card or by invoking the (owner, bus, triggers)
    /// overload).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Razzle-Dazzler with optional event bus + trigger manager.
    /// When <paramref name="eventBus"/> is supplied, a
    /// <see cref="TurnStartedEvent"/> handler resets the per-turn cast count
    /// (CR 500.1). When <paramref name="triggers"/> is supplied, the
    /// triggered ability is registered so the bus surfaces it as pending.
    /// When <paramref name="replacements"/> is supplied, the +1/+1 counter
    /// placement is routed through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season replacements can rewrite the count
    /// (CR 614).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(def, owner);

        card.SetController(owner);

        // ----------------------------------------------------------------
        // Per-turn spell-cast count. Closure shared between the trigger
        // predicate and the TurnStartedEvent reset handler.
        // ----------------------------------------------------------------
        var spellsCastThisTurn = new int[] { 0 };

        // "Whenever you cast your second spell each turn, put a +1/+1 counter
        // on this creature. It can't be blocked this turn." Predicate
        // increments the per-turn count on every SpellCastEvent owned by the
        // controller and only matches on the exact transition to 2 (CR 603.2
        // / 603.3 — the trigger only fires when its condition becomes true).
        // Casts beyond the second do not retrigger.
        var secondSpellCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Spell.Controller, owner)) return false;
            spellsCastThisTurn[0]++;
            return spellsCastThisTurn[0] == 2;
        });

        var effect = new Effect(
            "Razzle-Dazzler: put a +1/+1 counter on it and it can't be blocked this turn",
            () =>
            {
                // CR 122.1 — counter placed in real time; routed through
                // CountersService so replacements (Hardened Scales, etc.) apply.
                CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements);

                // CR 509.1c — "can't be blocked" restriction registered on
                // this creature's own ActiveEffects (queried by the combat
                // validator). expiresAtEndOfTurn: true → "this turn"
                // (CR 514.2). ActiveEffects is null in shape-only tests; the
                // counter half still applies.
                card.ActiveEffects?.Register(
                    new CombatRestrictionEffect(
                        CombatRestriction.CannotBeBlocked,
                        card,
                        expiresAtEndOfTurn: true));
            });

        var secondSpellTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: secondSpellCondition,
            effects: new IEffect[] { effect });

        card.AddAbility(secondSpellTrigger);

        // CR 500.1 — reset the per-turn count when a new turn starts.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => spellsCastThisTurn[0] = 0);
        }

        triggers?.RegisterTriggeredAbility(secondSpellTrigger);

        return card;
    }
}
