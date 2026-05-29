using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Experiment One (Gatecrash, {G}).
///
/// Creature — Human Ooze 1/1. Oracle text (Scryfall, verified):
///   "Evolve (Whenever a creature you control enters, if that creature
///    has greater power or toughness than this creature, put a +1/+1
///    counter on this creature.)
///    Remove two +1/+1 counters from this creature: Regenerate it."
///
/// Modern Hardened-Scales / GW-counters one-drop — an evolving body that
/// grows whenever you out-curve it and can shrug off removal twice over by
/// banking its counters into regeneration shields. Pairs with
/// <see cref="HardenedScalesFactory"/> (each evolve trigger banks an extra
/// counter) and the rest of the +1/+1-counter package.
///
/// ## Implemented (v1)
///
/// - <b>1/1 Creature — Human Ooze, mana cost {G}.</b>
///
/// - <b>Evolve — keyword ability (CR 702.100).</b> Reminder text:
///   "Whenever a creature you control enters, if that creature has greater
///    power or toughness than this creature, put a +1/+1 counter on this
///    creature." Wired as a <see cref="TriggeredAbility"/> whose
///    <see cref="EventTriggerCondition{T}"/> folds the printed
///    intervening-if "if that creature has greater power or toughness"
///    clause into the trigger predicate (CR 702.100b — evolve's condition
///    is evaluated as the creature enters, comparing the entering
///    creature's <i>current</i> power/toughness against Experiment One's
///    <i>current</i> power/toughness). The predicate gates on:
///      1. <see cref="CardMovedEvent.ToZone"/> == Battlefield.
///      2. The entering card is a <see cref="CardType.Creature"/>.
///      3. The entering card is NOT Experiment One itself (a creature's own
///         entry can never have power/toughness greater than itself — the
///         self-exclusion is implied by the strict greater-than comparison
///         but is asserted explicitly for clarity).
///      4. The entering creature is controlled by Experiment One's
///         controller ("a creature you control").
///      5. The entering creature's power &gt; Experiment One's power OR its
///         toughness &gt; Experiment One's toughness (CR 702.100b — strict
///         "greater", power <b>or</b> toughness).
///   On resolution the effect puts one +1/+1 counter on Experiment One via
///   <see cref="CountersService.Add"/> so a controlled
///   <see cref="HardenedScalesFactory">Hardened Scales</see> /
///   Doubling Season can rewrite the amount (CR 614) and the post-commit
///   <see cref="CounterAddedEvent"/> fires (Animation Module chain).
///   <para>
///   Power/toughness are read live via <see cref="Creature.Power"/> /
///   <see cref="Creature.Toughness"/> so existing counters and continuous
///   effects already on either creature are reflected at trigger time
///   (CR 702.100b — current values).
///   </para>
///
/// - <b>Regenerate — activated ability (CR 602.1 / CR 701.18).</b>
///   "Remove two +1/+1 counters from this creature: Regenerate it." The
///   sole cost is a <see cref="RemovePlusOnePlusOneCounterCost"/> for two
///   counters — there is no mana component (same regeneration-shield
///   primitive as <see cref="MortivoreFactory"/>'s "{B}: Regenerate
///   Mortivore", but paid with counters instead of mana). On resolution the
///   effect calls <see cref="Permanent.AddRegenerationShield"/>, creating a
///   regeneration shield (CR 701.15a) that the next destroy this turn
///   consumes — tapping Experiment One, removing it from combat, and
///   healing its damage (CR 701.18).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The evolve trigger is
///   attached for shape/dispatch observability but not registered with any
///   <see cref="TriggerManager"/>; the evolve counter placement uses the
///   direct <see cref="CountersService.Add"/> fallthrough (no
///   replacement-bus rewrites, no event publish). The regenerate ability is
///   fully attached and exercisable.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?, IEventBus?)"/>
///   — fully wired. The evolve trigger registers with the
///   <see cref="TriggerManager"/>; the counter placement routes through the
///   <see cref="ReplacementBus"/> and publishes
///   <see cref="CounterAddedEvent"/>.
/// </summary>
[CardName("Experiment One")]
public static class ExperimentOneFactory
{
    public const string CardName = "Experiment One";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>Number of +1/+1 counters the regenerate ability removes.</summary>
    public const int RegenerateCounterCost = 2;

    /// <summary>+1/+1 counters placed by an evolve trigger (CR 702.100c).</summary>
    public const int EvolveCounters = 1;

    /// <summary>
    /// Construct Experiment One with no live <see cref="TriggerManager"/>
    /// wiring. The evolve trigger is attached for shape/dispatch tests; the
    /// counter placement uses the direct <see cref="CountersService.Add"/>
    /// fallthrough (no replacement-bus rewrites, no event publish). The
    /// regenerate activated ability is fully attached.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Experiment One with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager. When supplied the evolve
    /// trigger registers for bus-driven firing (CR 603.2).</param>
    /// <param name="replacements">ReplacementBus. When supplied the evolve
    /// counter placement routes through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season can rewrite the count (CR 614).</param>
    /// <param name="eventBus">EventBus. When supplied the evolve counter
    /// placement publishes <see cref="CounterAddedEvent"/> so
    /// "+1/+1 counter was put on …" payoffs chain.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Ooze });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Evolve — CR 702.100.
        //   "Whenever a creature you control enters, if that creature has
        //    greater power or toughness than this creature, put a +1/+1
        //    counter on this creature."
        //
        // CR 702.100b folds the "if that creature has greater power or
        // toughness" intervening-if into the trigger condition: evolve only
        // triggers when the entering creature's current power OR current
        // toughness strictly exceeds Experiment One's at the time it enters.
        // We evaluate that comparison in the trigger predicate (which sees
        // the entering card via CardMovedEvent.Card) and place the counter
        // in the effect on resolution (CR 702.100c).
        // ----------------------------------------------------------------
        var evolveEffect = new Effect(
            $"{CardName}: evolve — put a +1/+1 counter on self (CR 702.100c)",
            () => CountersService.Add(
                card, CounterType.PlusOnePlusOne, EvolveCounters, replacements, eventBus));

        var evolveTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                if (e.ToZone != ZoneType.Battlefield) return false;
                if (e.Card is not Creature entering) return false;
                if (ReferenceEquals(entering, card)) return false; // own entry never larger

                // "a creature you control" — compare against Experiment One's
                // live controller (control-change effects route correctly).
                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(entering.Controller, controller)) return false;

                // CR 702.100b — strict greater in power OR toughness, current
                // values (so existing counters / continuous effects count).
                return entering.Power > card.Power
                    || entering.Toughness > card.Toughness;
            }),
            effects: new IEffect[] { evolveEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(evolveTrigger);
        triggers?.RegisterTriggeredAbility(evolveTrigger);

        // ----------------------------------------------------------------
        // Regenerate — CR 602.1 / CR 701.18.
        //   "Remove two +1/+1 counters from this creature: Regenerate it."
        // The only cost is removing two +1/+1 counters (no mana). On resolve
        // a regeneration shield is created on Experiment One
        // (Permanent.AddRegenerationShield — CR 701.15a), consumed by the
        // next destroy this turn (tap, remove from combat, heal damage —
        // CR 701.18). Same shield primitive as MortivoreFactory.
        // ----------------------------------------------------------------
        var regenerateEffect = new Effect(
            $"{CardName}: regenerate self (CR 701.18)",
            () => card.AddRegenerationShield());

        var regenerateAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new RemovePlusOnePlusOneCounterCost(card, RegenerateCounterCost) },
            effects: new IEffect[] { regenerateEffect });

        card.AddAbility(regenerateAbility);

        return card;
    }
}
