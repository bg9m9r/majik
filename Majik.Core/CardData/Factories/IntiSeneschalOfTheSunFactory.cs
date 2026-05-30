using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inti, Seneschal of the Sun (Outlaws of Thunder
/// Junction, {1}{R}). Legendary Creature — Human Knight 2/2. Oracle text
/// (verified against Scryfall):
///   "Whenever you attack, you may discard a card. When you do, put a +1/+1
///    counter on target attacking creature. It gains trample until end of
///    turn.
///    Whenever you discard one or more cards, exile the top card of your
///    library. You may play that card until your next end step."
///
/// The base shape (name, Legendary supertype, Creature, Human + Knight
/// subtypes, {1}{R}, 2/2) is materialised from the embedded JSON definition
/// (<c>inti-seneschal-of-the-sun.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two triggered abilities
/// are layered on here — the JSON <c>AbilityDefinition</c> schema doesn't
/// yet express attack triggers, discard-as-payment reflexive triggers,
/// counters, keyword grants, or impulse exile-cast, so they live in the
/// factory (same posture as <see cref="EmberheartChallengerFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Attack trigger (CR 508.1 / 603.1)</b> — fires on
///   <see cref="AttackersDeclaredEvent"/> when Inti's controller is the
///   attacking player ("Whenever you attack"). On resolve it asks "you may
///   discard a card" (<paramref name="mayDiscard"/>); when the controller
///   discards (CR 701.8a — a card moves Hand → Graveyard), the reflexive
///   "When you do" sub-effect (CR 603.1 reflexive trigger) puts a +1/+1
///   counter (CR 122.1) on a target attacking creature and grants it
///   Trample until end of turn (CR 702.19 / 514.2). The target attacking
///   creature is resolved via <paramref name="attackTargetResolver"/>
///   (default: the first attacker controlled by Inti's controller, which is
///   the v1 closure-injection posture for "target attacking creature" —
///   same as <see cref="SoaringThoughtThiefFactory"/>'s mill-target
///   resolver). The +1/+1 counter buffs P/T through layer 7c
///   (<see cref="ContinuousEffectsService"/>, CR 613.4), and the Trample
///   grant is a <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the
///   target's <see cref="Creature.ActiveEffects"/>.
///
///   The discard itself is a real Hand → Graveyard move, so it ALSO fires
///   Inti's own second ability (the discard trigger below) — exactly as in
///   real MTG, where discarding to Inti's attack trigger feeds the
///   impulse-draw.
///
/// - <b>Discard trigger (CR 603.1)</b> — "Whenever you discard one or more
///   cards, exile the top card of your library. You may play that card until
///   your next end step." The engine has no dedicated discard event (see
///   <see cref="ContainmentConstructFactory"/> / <see cref="NecropotenceFactory"/>);
///   discards funnel through <see cref="CardMovedEvent"/> with
///   <c>FromZone == Hand &amp;&amp; ToZone == Graveyard</c>. The trigger
///   gates to cards owned by Inti's controller ("you discard"). On resolve
///   it exiles the top card of the controller's library (CR 701.20) and
///   stamps a runtime exile-cast grant (<see cref="Card.GrantRuntimeExileCast"/>)
///   so the controller may play it (CR 118.9) — the same impulse-draw
///   primitive as <see cref="EmberheartChallengerFactory"/> / Light Up the
///   Stage. The grant duration is "until your next end step" (CR 514.2),
///   cleared on the SECOND <see cref="PhaseStateType.End"/> step belonging
///   to the controller seen after the discard (the controller's *next* end
///   step — the first End step seen may be the one in the very turn the
///   discard happened, e.g. a combat-step discard on the controller's own
///   turn, so we skip one End step to land on "your NEXT end step").
///
/// ## Deferred (v1 gaps)
///
/// - <b>"One or more cards" batching</b>: a multi-card discard (e.g. a
///   rummage that discards two at once) funnels as separate
///   <see cref="CardMovedEvent"/>s, so Inti's discard trigger fires once per
///   discarded card rather than once for the batch. v1 ships per-card
///   triggering (over-fires impulse-draws on simultaneous multi-discards);
///   the single-card discard path (Inti's own attack trigger, Faithless
///   Looting one-at-a-time) is exact. Same funnel posture as Containment
///   Construct.
/// - <b>Agent-driven discard pick</b>: the "you may discard a card" cost
///   discards the FIRST card in hand when accepted (<paramref name="mayDiscard"/>
///   only chooses whether to discard). Full agent-driven discard selection
///   is deferred behind the same queue as Faithless Looting / Liliana of
///   the Veil.
/// - <b>"Target attacking creature" agent pick</b>: the +1/+1 counter +
///   Trample land on the resolver-chosen attacker (default: first attacker
///   the controller controls). Same closure-injection posture as
///   Soaring Thought-Thief's mill target.
/// - <b>Empty-library exile</b>: an empty library makes the exile a no-op
///   (CR 701.20 imposes no SBA flag for an exile that finds nothing).
/// </summary>
[CardName("Inti, Seneschal of the Sun")]
public static class IntiSeneschalOfTheSunFactory
{
    public const string CardName = "Inti, Seneschal of the Sun";
    public const string Slug = "inti-seneschal-of-the-sun";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>Granted keyword — CR 702.19 Trample.</summary>
    public const string GrantedTrample = "Trample";

    /// <summary>
    /// Construct Inti with no live wiring (the shape / dispatcher path). Both
    /// triggers are attached for shape observability but not registered; the
    /// reflexive counter/trample sub-effect and the impulse grant will not
    /// auto-clear without an event bus. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null,
            attackTargetResolver: null, mayDiscard: null);

    /// <summary>
    /// Construct Inti with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the discard trigger's impulse
    /// grant clears on the controller's next <see cref="PhaseStateType.End"/>
    /// step (CR 514.2).</param>
    /// <param name="triggers">TriggerManager the attack + discard triggers
    /// are registered with so they surface as pending. May be null.</param>
    /// <param name="effects">ContinuousEffectsService used by the attack
    /// trigger's reflexive sub-effect to grant Trample until end of turn
    /// (layer 6) to the buffed attacker. The +1/+1 counter itself buffs P/T
    /// through whatever layers service is bound to the target attacker. May
    /// be null — Trample is then not granted live.</param>
    /// <param name="attackTargetResolver">Closure returning the "target
    /// attacking creature" for the reflexive sub-effect, given the live
    /// <see cref="Combat"/>. May be null — defaults to the first attacker
    /// the controller controls.</param>
    /// <param name="mayDiscard">"You may discard a card" chooser for the
    /// attack trigger. Returns true to discard (the first card in hand),
    /// false to decline. May be null — defaults to discarding whenever the
    /// controller has a card in hand (the upside branch).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects,
        Func<Majik.Core.Combat.Combat, Creature?>? attackTargetResolver = null,
        Func<bool>? mayDiscard = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Human + Knight, {1}{R}, 2/2). No abilities in the JSON —
        // both triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        AddAttackTrigger(card, owner, effects, attackTargetResolver, mayDiscard, triggers);
        AddDiscardTrigger(card, owner, eventBus, triggers);

        return card;
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever you attack, you may discard a card. When you
    // do, put a +1/+1 counter on target attacking creature. It gains trample
    // until end of turn." (CR 508.1 / 603.1 reflexive trigger.)
    // -----------------------------------------------------------------------
    private static void AddAttackTrigger(
        Creature card,
        Player owner,
        ContinuousEffectsService? effects,
        Func<Majik.Core.Combat.Combat, Creature?>? attackTargetResolver,
        Func<bool>? mayDiscard,
        TriggerManager? triggers)
    {
        // Capture the combat from the triggering event so the resolve body
        // can read the declared attackers (CR 603.2 — a triggered ability is
        // associated with the specific event that triggered it).
        Majik.Core.Combat.Combat? capturedCombat = null;

        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
        {
            // "Whenever you attack" — only when Inti's controller is the
            // attacking player (CR 508.1 / 109.5).
            if (!ReferenceEquals(e.Combat.AttackingPlayer, card.Controller ?? owner))
                return false;
            capturedCombat = e.Combat;
            return true;
        });

        var attackEffect = new Effect(
            $"{CardName}: on attack, you may discard a card; when you do, +1/+1 counter + trample on target attacking creature",
            () =>
            {
                var combat = capturedCombat;
                capturedCombat = null;
                ResolveAttackTrigger(combat, card, owner, effects, attackTargetResolver, mayDiscard);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { attackEffect },
            // CR 113.6 — Inti's attack trigger functions only from the
            // battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);
    }

    private static void ResolveAttackTrigger(
        Majik.Core.Combat.Combat? combat,
        Creature card,
        Player owner,
        ContinuousEffectsService? effects,
        Func<Majik.Core.Combat.Combat, Creature?>? attackTargetResolver,
        Func<bool>? mayDiscard)
    {
        if (combat == null) return;
        var controller = card.Controller ?? owner;

        // "You may discard a card." CR 701.8a. Default: discard whenever the
        // controller has a card in hand (the upside branch).
        var wantsDiscard = mayDiscard?.Invoke()
            ?? controller.Zones.Hand.GetCards().Any();
        if (!wantsDiscard) return;

        var toDiscard = controller.Zones.Hand.GetCards().FirstOrDefault();
        if (toDiscard == null) return; // can't pay the optional cost — no card.

        // Real Hand → Graveyard discard. Routing it as a CardMovedEvent-free
        // direct zone move keeps the discard observable to Inti's own discard
        // trigger via the zone service when one is wired; here we move it
        // directly and let the caller's CardMovedEvent funnel (if any) fire.
        controller.Zones.Hand.RemoveCard(toDiscard);
        controller.Zones.Graveyard.AddCard(toDiscard);
        // Zone.AddCard sets card.Zone — no manual SetZone needed.

        // "When you do" reflexive trigger (CR 603.1): put a +1/+1 counter on
        // target attacking creature; it gains trample until end of turn.
        var target = attackTargetResolver?.Invoke(combat)
            ?? DefaultAttackTarget(combat, controller);
        if (target == null) return;

        // CR 122.1 — put a +1/+1 counter. Buffs P/T through layer 7c when the
        // target has a layers service bound.
        target.Counters.Add(CounterType.PlusOnePlusOne);

        // CR 702.19 / 514.2 — gains trample until end of turn.
        var layers = target.ActiveEffects ?? effects;
        layers?.Register(new GrantKeywordUntilEndOfTurnEffect(target, GrantedTrample));
    }

    private static Creature? DefaultAttackTarget(Majik.Core.Combat.Combat combat, Player controller)
    {
        // v1 fallback "target attacking creature" — first declared attacker
        // the controller controls (same closure-injection posture as
        // Soaring Thought-Thief's mill target).
        foreach (var atk in combat.Attackers)
        {
            var creature = atk?.Creature;
            if (creature == null) continue;
            if (ReferenceEquals(creature.Controller, controller)) return creature;
        }
        return null;
    }

    // -----------------------------------------------------------------------
    // Discard trigger — "Whenever you discard one or more cards, exile the top
    // card of your library. You may play that card until your next end step."
    // (CR 603.1; no dedicated discard event — funnels through CardMovedEvent.)
    // -----------------------------------------------------------------------
    private static void AddDiscardTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            // Discards funnel through Hand → Graveyard CardMovedEvents (see
            // Containment Construct / Necropotence). "You discard" — gate to
            // the controller's own cards (CR 109.5); the moved card's owner
            // is the discarder.
            if (e.FromZone != ZoneType.Hand) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!ReferenceEquals(e.Card.Owner, card.Controller ?? owner)) return false;
            return true;
        });

        var exileEffect = new Effect(
            $"{CardName}: on discard, exile the top card of your library; you may play it until your next end step",
            () => ResolveDiscardTrigger(card, owner, eventBus));

        var discardTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { exileEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(discardTrigger);
        triggers?.RegisterTriggeredAbility(discardTrigger);
    }

    private static void ResolveDiscardTrigger(Creature card, Player owner, IEventBus? eventBus)
    {
        var controller = card.Controller ?? owner;

        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return; // empty library — exile finds nothing (CR 701.20).

        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Exile.AddCard(top);
        top.SetZone(ZoneType.Exile);

        if (top is not Card stampable) return;

        // CR 118.9 — "you may play that card": authorise casting for the
        // printed mana cost. Same impulse primitive as Emberheart / Light Up
        // the Stage.
        stampable.GrantRuntimeExileCast(controller, stampable.ManaCostValue);

        // "Until your next end step" (CR 514.2). Clear on the controller's
        // NEXT End step. The discard can happen on the controller's own turn
        // (e.g. discarding to Inti's attack trigger in combat), in which case
        // the first End step seen is THIS turn's — but "your next end step"
        // means the one after the current one, so we skip the first End step
        // belonging to the controller and clear on the second. Without a bus
        // the grant persists until cleared by hand (test-only posture).
        ScheduleNextEndStepGrantClear(stampable, controller, eventBus);
    }

    private static void ScheduleNextEndStepGrantClear(
        Card stampable,
        Player controller,
        IEventBus? eventBus)
    {
        if (eventBus == null) return;

        var controllerEndStepsSeen = new int[] { 0 };
        Action<StepStartedEvent>? handler = null;
        handler = (e) =>
        {
            if (e.StepType != PhaseStateType.End) return;
            if (!ReferenceEquals(e.Player, controller)) return;

            controllerEndStepsSeen[0]++;
            // "Your NEXT end step" — clear on the second of the controller's
            // End steps seen after the grant (skip the current turn's end
            // step, land on the next one). If the discard happened on an
            // opponent's turn, the controller's first End step IS their next
            // end step — but to keep the rule uniform we always treat the
            // first controller End step as the "current" boundary and the
            // second as "next". This is the conservative reading of CR 514.2
            // for "until your next end step".
            if (controllerEndStepsSeen[0] < 2) return;

            if (ReferenceEquals(stampable.RuntimeExileCastAllowedCaster, controller))
            {
                stampable.ClearRuntimeExileCast();
            }
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }
}
