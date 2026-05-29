using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heartfire Hero (Bloomburrow, {R}).
/// Creature — Mouse Soldier 1/1. Oracle text (verified against Scryfall):
///   "Valiant — Whenever this creature becomes the target of a spell or
///    ability you control for the first time each turn, put a +1/+1 counter
///    on it.
///    When this creature dies, it deals damage equal to its power to each
///    opponent."
///
/// The base shape (name, Creature, Mouse + Soldier subtypes, {R}, 1/1) is
/// materialised from the embedded JSON definition (<c>heartfire-hero.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (Valiant first-target trigger, dies-damage trigger) are layered on here —
/// the JSON <c>AbilityDefinition</c> schema doesn't yet express the Valiant
/// first-target trigger or the LKI-power dies trigger, so they live in the
/// factory (same posture as <see cref="EmberheartChallengerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Valiant (CR 603.6c / 115.6 / 603.2-3)</b> — a
///   <see cref="TargetsChosenEvent"/> trigger that fires the FIRST time each
///   turn the hero becomes the target of a spell or ability ITS CONTROLLER
///   controls. "you control" is read off
///   <see cref="Majik.Core.Stack.IStackObject.Controller"/> on the event's
///   stack object; <see cref="TargetsChosenEvent"/> is published by both
///   <see cref="Majik.Core.Services.SpellCaster"/> and
///   <see cref="Majik.Core.Services.AbilityActivator"/>, so "spell or
///   ability" is covered automatically (same attachment point as
///   <see cref="EmberheartChallengerFactory"/>). The once-per-turn cap is a
///   boolean gate reset by a <see cref="TurnStartedEvent"/> handler
///   (CR 500.1). On resolve: put a single +1/+1 counter on the hero
///   (CR 122 — counters; reflected in <see cref="Creature.Power"/> /
///   <see cref="Creature.GetToughness"/> via the layer compute when a
///   <see cref="ContinuousEffectsService"/> is bound to
///   <see cref="Card.ActiveEffects"/>).
/// - <b>Dies trigger (CR 603.6d / 700.4)</b> — a
///   <see cref="CardMovedEvent"/> trigger (Battlefield → Graveyard, the
///   moved card is this creature) that deals damage equal to the hero's
///   power to each opponent. "its power" is read as last-known information
///   (CR 603.10): the <see cref="Creature"/> instance retains its counters
///   and its <see cref="Card.ActiveEffects"/> reference after leaving the
///   battlefield, so <see cref="Creature.Power"/> at resolution reflects the
///   power the hero had immediately before it died (including any Valiant
///   +1/+1 counters). Damage to each opponent goes through
///   <see cref="Fx.DealDamage(object, int)"/> (Player → LoseLife, CR 119).
///   "each opponent" is supplied by an optional
///   <paramref name="opponentResolver"/> (same caller-supplied-opponents
///   posture as <see cref="LurkingRoperFactory"/> — the engine has no
///   global opponents accessor on <see cref="Player"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>"each opponent" enumeration</b>: supplied by the caller's resolver
///   lambda; the single-arg <see cref="Create(Player)"/> shape no-ops the
///   damage (no resolver). Matches Lurking Roper / Omnath "each opponent"
///   posture.
/// - <b>Dying-creature LKI for the controller of the dies trigger</b>:
///   CR 603.10 — the trigger's controller is read from LKI at death. v1
///   reads <see cref="Permanent.Controller"/> / the captured owner directly
///   (same posture as <see cref="FalkenrathNobleFactory"/>).
/// </summary>
[CardName("Heartfire Hero")]
public static class HeartfireHeroFactory
{
    public const string CardName = "Heartfire Hero";
    public const string Slug = "heartfire-hero";

    /// <summary>
    /// Construct Heartfire Hero with no live wiring. Both triggers (Valiant +
    /// dies) are attached for shape observability; the Valiant once-per-turn
    /// reset handler is not installed (no event bus), and the dies trigger
    /// has no opponent resolver so its damage side is a no-op. Suitable for
    /// dispatcher / structural tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null, opponentResolver: null);

    /// <summary>
    /// Construct Heartfire Hero with the trigger-driving services wired but no
    /// opponent resolver — the Valiant counter side is live; the dies-damage
    /// side is a no-op (no opponents supplied). Convenience overload for
    /// Valiant-focused tests.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects) =>
        Create(owner, eventBus, triggers, effects, opponentResolver: null);

    /// <summary>
    /// Construct Heartfire Hero with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, a <see cref="TurnStartedEvent"/>
    /// handler resets the Valiant once-per-turn gate (CR 500.1).</param>
    /// <param name="triggers">TriggerManager the Valiant + dies triggers are
    /// registered with so they surface as pending. May be null.</param>
    /// <param name="effects">ContinuousEffectsService bound onto the card so
    /// the Valiant +1/+1 counter is reflected in
    /// <see cref="Creature.Power"/> / toughness via the layer compute
    /// (CR 122 / 613). When null, the counter is still added but
    /// <see cref="Creature.GetPower"/> falls back to base P/T.</param>
    /// <param name="opponentResolver">Supplies the opponents the dies trigger
    /// damages (CR 603.6d). May be null — the damage side no-ops.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects,
        Func<IEnumerable<Player>?>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Mouse + Soldier subtypes, {R}, 1/1). The JSON carries no abilities
        // — Valiant / dies are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // CR 603.6c / 115.6 — Valiant first-target trigger.
        var valiant = BuildValiant(card, owner, eventBus);
        card.AddAbility(valiant);
        triggers?.RegisterTriggeredAbility(valiant);

        // CR 603.6d — dies trigger.
        var dies = BuildDies(card, owner, opponentResolver);
        card.AddAbility(dies);
        triggers?.RegisterTriggeredAbility(dies);

        return card;
    }

    /// <summary>
    /// Build the Valiant trigger — "Whenever this creature becomes the target
    /// of a spell or ability you control for the first time each turn, put a
    /// +1/+1 counter on it." (CR 603.6c / 115.6 / 603.2-3).
    /// </summary>
    private static TriggeredAbility BuildValiant(Creature card, Player owner, IEventBus? eventBus)
    {
        // Once-per-turn gate, shared between the predicate (sets it on the
        // first matching event each turn) and the TurnStartedEvent reset
        // handler. Boxed in a single-element array so the closures mutate a
        // shared cell. CR 603.2 / 603.3 — "for the first time each turn".
        var firedThisTurn = new bool[] { false };

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            if (firedThisTurn[0]) return false;

            // "you control" — the spell or ability must be controlled by the
            // hero's controller (CR 109.5 / 603.6c). TargetsChosenEvent is
            // published by both SpellCaster and AbilityActivator, so this
            // covers "a spell or ability you control" uniformly.
            if (!ReferenceEquals(e.StackObject.Controller, card.Controller)) return false;

            // "this creature becomes the target" — one of the chosen targets
            // is the hero itself (CR 115.6).
            foreach (var t in e.Targets)
            {
                if (t.TargetType != TargetType.Permanent && t.TargetType != TargetType.Card)
                {
                    continue;
                }
                if (t is not Target concrete) continue;
                if (!ReferenceEquals(concrete.TargetObject, card)) continue;

                firedThisTurn[0] = true;
                return true;
            }

            return false;
        });

        var counterEffect = new Effect(
            "Valiant — put a +1/+1 counter on this creature",
            () => card.Counters.Add(CounterType.PlusOnePlusOne)); // CR 122

        var valiant = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        // CR 500.1 — reset the once-per-turn gate at the start of each turn.
        eventBus?.Subscribe<TurnStartedEvent>(_ => firedThisTurn[0] = false);

        return valiant;
    }

    /// <summary>
    /// Build the dies trigger — "When this creature dies, it deals damage
    /// equal to its power to each opponent." (CR 603.6d / 700.4 / 603.10).
    /// </summary>
    private static TriggeredAbility BuildDies(
        Creature card, Player owner, Func<IEnumerable<Player>?>? opponentResolver)
    {
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            // "this creature dies" — the moved card is the hero itself.
            return ReferenceEquals(e.Card, card);
        });

        var damageEffect = new Effect(
            $"{CardName} dies: deal damage equal to its power to each opponent",
            () =>
            {
                // CR 603.10 — "its power" is last-known information: the
                // Creature instance retains its counters + ActiveEffects
                // reference after leaving the battlefield, so Power here is
                // the power the hero had immediately before it died.
                var power = card.Power;
                if (power <= 0) return;

                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;

                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, owner)) continue;
                    Fx.DealDamage(opp, power); // CR 119 — Player → LoseLife
                }
            });

        // CR 603.6d — a self-naming dies trigger must remain active in the
        // graveyard so the hero's OWN death still resolves the damage.
        return new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
    }
}
