using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Emberheart Challenger (Bloomburrow, {1}{R}).
/// Creature — Mouse Warrior 2/2. Oracle text (verified against Scryfall):
///   "Haste
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    Valiant — Whenever this creature becomes the target of a spell or
///    ability you control for the first time each turn, exile the top card
///    of your library. Until end of turn, you may play that card."
///
/// The base shape (name, Creature, Mouse + Warrior subtypes, {1}{R}, 2/2)
/// is materialised from the embedded JSON definition
/// (<c>emberheart-challenger.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (Haste keyword, Prowess, Valiant) are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express keyword markers,
/// Prowess, or the Valiant first-target trigger, so they live in the
/// factory (same posture as <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Haste (CR 702.10)</b> — <see cref="KeywordAbility"/> marker consumed
///   by CombatValidator / CombatAbilities (same wiring as Slickshot
///   Show-Off's Haste).
/// - <b>Prowess (CR 702.108)</b> — built via
///   <see cref="ProwessFactory.Build"/>: an on-cast trigger over
///   <see cref="SpellCastEvent"/> filtered to (controller + non-Creature
///   spell) that registers a +1/+1-until-end-of-turn pump on the
///   <see cref="ContinuousEffectsService"/> (Layer 7c, CR 514.2). Only wired
///   when a layers service is supplied.
/// - <b>Valiant (CR 603.6c / 115.6 / 603.2-3)</b> — a
///   <see cref="TargetsChosenEvent"/> trigger that fires the FIRST time each
///   turn Emberheart becomes the target of a spell or ability ITS CONTROLLER
///   controls. "you control" is read off
///   <see cref="Majik.Core.Stack.IStackObject.Controller"/> on the event's
///   stack object (the casting spell or activated/triggered ability) —
///   <see cref="TargetsChosenEvent"/> is published by both
///   <see cref="Majik.Core.Services.SpellCaster"/> and
///   <see cref="Majik.Core.Services.AbilityActivator"/>, so "spell or
///   ability" is covered automatically (same attachment point as
///   <see cref="NaduWingedWisdomFactory"/>). The once-per-turn cap mirrors
///   Nadu's per-turn counter reset by a <see cref="TurnStartedEvent"/>
///   handler (CR 500.1). On resolve: exile the top card of the controller's
///   library (CR 701.20) and stamp a runtime exile-cast grant
///   (<see cref="Card.GrantRuntimeExileCast"/>) so the controller may play
///   it until end of turn (CR 118.9) — same impulse-draw primitive as
///   <see cref="LightUpTheStageFactory"/> / <see cref="RecklessImpulseFactory"/>,
///   but the duration is "until end of turn" (a single Cleanup, CR 514.2)
///   rather than "until end of your next turn".
///
/// ## Deferred (v1 gaps)
/// - <b>"May play that card" includes lands</b>: the runtime exile-cast
///   grant authorises casting; an exiled land would need a parallel "play
///   this land from exile" grant. v1 ships the spell-only authorisation,
///   matching the LightUpTheStage / Reckless Impulse posture.
/// - <b>Empty-library exile</b>: if the library is empty the exile is a
///   no-op (CR 701.20 imposes no SBA flag for an exile that finds nothing).
/// - <b>Agent "may" on the play permission</b>: the grant is always stamped
///   ("you MAY play that card" is a permission, not a forced action — the
///   controller simply may decline to cast it later), so no agent prompt is
///   needed at resolution.
/// </summary>
[CardName("Emberheart Challenger")]
public static class EmberheartChallengerFactory
{
    public const string CardName = "Emberheart Challenger";
    public const string Slug = "emberheart-challenger";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Emberheart Challenger with no live wiring. Haste + the
    /// Valiant trigger are attached for shape observability; Prowess is NOT
    /// attached (it needs a <see cref="ContinuousEffectsService"/> for its
    /// pump), and the Valiant trigger's once-per-turn reset handler is not
    /// installed (no event bus). Suitable for dispatcher / structural tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Emberheart Challenger with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, a <see cref="TurnStartedEvent"/>
    /// handler resets the Valiant once-per-turn counter (CR 500.1), and the
    /// Valiant resolve effect schedules its "until end of turn" exile-cast
    /// cleanup on the next Cleanup step (CR 514.2).</param>
    /// <param name="triggers">TriggerManager the Prowess + Valiant triggers
    /// are registered with so they surface as pending. May be null.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess +1/+1
    /// pump (Layer 7c). When null, Prowess is not wired (the keyword needs a
    /// layers service to express its pump).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Mouse + Warrior subtypes, {1}{R}, 2/2). The JSON carries no
        // abilities — Haste / Prowess / Valiant are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.10 — Haste keyword marker.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 702.108 — Prowess. The trigger is ALWAYS attached for shape
        // observability (same posture as Slickshot Show-Off's cast-pump
        // trigger). ProwessFactory.Build needs a non-null layers service to
        // register its +1/+1 pump (Layer 7c); when the caller supplies one we
        // bind it onto the card and register the pump live, otherwise we hand
        // the builder a throwaway service so the trigger shape still attaches
        // but the pump silently no-ops on execute (no service bound onto the
        // card's ActiveEffects).
        // The busless fallback here is a THROWAWAY: it is handed to
        // ProwessFactory.Build only to satisfy its non-null layers requirement
        // and is NEVER bound onto card.ActiveEffects (only the bus-wired live
        // `effects` is, line below). So no CDA ever reads through this instance
        // — no stale-cache exposure, no bus wiring needed. The pump simply
        // no-ops in the no-service path.
        var prowessEffects = effects ?? new ContinuousEffectsService();
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }
        var prowess = ProwessFactory.Build(card, prowessEffects);
        card.AddAbility(prowess);
        if (effects != null)
        {
            triggers?.RegisterTriggeredAbility(prowess);
        }

        // CR 603.6c / 115.6 — Valiant first-target trigger.
        var valiant = BuildValiant(card, owner, eventBus);
        card.AddAbility(valiant);
        triggers?.RegisterTriggeredAbility(valiant);

        return card;
    }

    /// <summary>
    /// Build the Valiant trigger — "Whenever this creature becomes the target
    /// of a spell or ability you control for the first time each turn, exile
    /// the top card of your library. Until end of turn, you may play that
    /// card." (CR 603.6c / 115.6 / 603.2-3).
    /// </summary>
    private static TriggeredAbility BuildValiant(Creature card, Player owner, IEventBus? eventBus)
    {
        // Once-per-turn gate. Shared between the predicate (which sets it on
        // the first matching event each turn) and the TurnStartedEvent reset
        // handler. Boxed in a single-element array so the closures mutate a
        // shared cell. CR 603.2 / 603.3 — "for the first time each turn".
        var firedThisTurn = new bool[] { false };

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            // First time each turn only.
            if (firedThisTurn[0]) return false;

            // "you control" — the spell or ability must be controlled by
            // Emberheart's controller (CR 109.5 / 603.6c). TargetsChosenEvent
            // is published by both SpellCaster and AbilityActivator, so this
            // covers "a spell or ability you control" uniformly.
            if (!ReferenceEquals(e.StackObject.Controller, card.Controller)) return false;

            // "this creature becomes the target" — one of the chosen targets
            // is Emberheart itself (CR 115.6).
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

        var exileEffect = new Effect(
            "Valiant — exile the top card of your library; until end of turn you may play that card",
            () =>
            {
                var controller = card.Controller ?? owner;

                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty library — exile finds nothing (CR 701.20)

                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Exile.AddCard(top);
                top.SetZone(ZoneType.Exile);

                if (top is not Card concrete) return;

                // CR 118.9 — "you may play that card" with no alternate-cost
                // rider: the grant authorises casting for the printed mana
                // cost. Same impulse-draw primitive as LightUpTheStage.
                concrete.GrantRuntimeExileCast(controller, concrete.ManaCostValue);

                // CR 514.2 — "until end of turn" is a SINGLE Cleanup: clear
                // the grant at the next Cleanup belonging to any player after
                // this resolution. (Valiant can trigger on an opponent's turn
                // because "a spell or ability you control" may be cast at
                // instant speed, so we clear on the first Cleanup seen rather
                // than gating on the controller — CR 514.2 ends the duration
                // at the current turn's cleanup regardless of whose turn it
                // is.) Without a bus the grant persists until cleared by hand.
                if (eventBus == null) return;

                Action<StepStartedEvent>? handler = null;
                handler = (se) =>
                {
                    if (se.StepType != PhaseStateType.Cleanup) return;
                    concrete.ClearRuntimeExileCast();
                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            });

        var valiant = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { exileEffect },
            activeZones: new[] { ZoneType.Battlefield });

        // CR 500.1 — reset the once-per-turn gate at the start of each turn.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => firedThisTurn[0] = false);
        }

        return valiant;
    }
}
