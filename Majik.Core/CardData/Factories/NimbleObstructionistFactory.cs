using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Stack;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nimble Obstructionist (Modern Horizons,
/// <c>{2}{U}</c>).
///
/// Creature — Bird Wizard 3/1. Oracle text (Scryfall, MH1):
///   "Flash
///    Flying
///    Cycling {2}{U} ({2}{U}, Discard this card: Draw a card.)
///    When you cycle this card, counter target activated or triggered
///    ability you don't control."
///
/// ## Implemented (v1)
/// - <b>Creature — Bird Wizard {2}{U} 3/1</b> with owner / controller
///   wiring (CR 205.3m subtypes).
/// - <b>Flash</b> (CR 702.8) + <b>Flying</b> (CR 702.9) wired as
///   <see cref="KeywordAbility"/> markers — same posture as
///   <see cref="TishanasTidebinderFactory"/> (Flash) /
///   <see cref="CuratorOfMysteriesFactory"/> (Flying). Flying is consumed
///   by <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>; Flash
///   is read by the casting-timing surface.
/// - <b>Cycling {2}{U}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{2}{U}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers the <see cref="DiscardSelfCost"/> hand-zone
///   gate (CR 702.32a) onto the cost stack, and on resolve publishes
///   <see cref="CardCycledEvent"/> for CR 702.32d subscribers.
/// - <b>"When you cycle this card, counter target activated or triggered
///   ability you don't control." trigger</b> (CR 702.32d / CR 603.6):
///   wired as a <see cref="TriggeredAbility"/> over
///   <see cref="EventTriggerCondition{CardCycledEvent}"/> gated to
///   <c>ReferenceEquals(e.Card, card)</c> (the printed self-cycle gate)
///   AND <c>e.Player == card.Controller</c> ("you cycle" — CR 109.5).
///   <see cref="TriggeredAbility.ActiveZones"/> = {Graveyard} because the
///   cycling resolve body discards the card before publishing
///   <see cref="CardCycledEvent"/>, so the trigger must function from the
///   graveyard (same posture as <see cref="DecreeOfPainFactory"/>'s
///   on-cycle sweep). The trigger declares one mandatory
///   "target activated or triggered ability you don't control"
///   <see cref="TargetRequest"/>.
///
///   <para>
///   Resolve re-checks legality at resolution (CR 608.2b) and counters the
///   chosen ability via <see cref="OracleSpellBinder.RemoveFromStack"/>
///   (CR 701.5b — countered triggered/activated abilities cease to exist,
///   no graveyard hop). The predicate accepts both
///   <see cref="ITriggeredAbility"/> and <see cref="IActivatedAbility"/>;
///   <see cref="IManaAbility"/> can never appear here because mana
///   abilities don't use the stack (CR 605.1). The "you don't control"
///   gate is enforced via <see cref="IStackObject.Controller"/> — an
///   ability the cycling player controls is an illegal target and the
///   resolve is a clean no-op. Same counter primitive as
///   <see cref="TishanasTidebinderFactory"/>.
///   </para>
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Cycling ability +
///   on-cycle trigger attached for shape inspection; no event bus (no
///   <see cref="CardCycledEvent"/> publication), no
///   <see cref="TriggerManager"/> registration, counter resolve is a
///   no-op (no live stack). Suitable for dispatcher / shape tests. This is
///   the overload the generated <c>[CardName]</c> dispatch invokes.
/// - <see cref="Create(Player, Majik.Core.Stack.Stack?)"/> — counter-
///   resolution path. The on-cycle trigger's resolve body removes the
///   chosen ability from the supplied stack.
/// - <see cref="Create(Player, Majik.Core.Stack.Stack?, TriggerManager?, IEventBus?)"/>
///   — fully wired. The on-cycle trigger is registered so a self-cycle
///   <see cref="CardCycledEvent"/> auto-queues the counter; cycling
///   resolve publishes against the supplied bus.
///
/// CR rule references: 205.3m (Bird / Wizard subtypes), 603.6 (triggered
/// ability), 605.1 (mana abilities don't use the stack), 608.2b
/// (resolution-time legality recheck), 701.5b (countering an ability),
/// 702.8 (Flash), 702.9 (Flying), 702.32 (Cycling), 702.32d ("When you
/// cycle this card" trigger).
/// </summary>
[CardName("Nimble Obstructionist")]
public static class NimbleObstructionistFactory
{
    public const string CardName = "Nimble Obstructionist";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 3;
    public const int Toughness = 1;
    public const string CyclingCost = "{2}{U}";

    /// <summary>
    /// Construct Nimble Obstructionist with no live wiring. Cycling ability
    /// + on-cycle counter trigger attached for shape inspection; no event
    /// bus / stack / trigger registration. This is the overload the
    /// generated <c>[CardName]</c> dispatch invokes.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Nimble Obstructionist with a live <paramref name="stack"/>
    /// so the on-cycle trigger's resolve body can counter the chosen
    /// ability. No event bus / trigger registration (shape + resolve path
    /// only — mirrors <see cref="TishanasTidebinderFactory.Create(Player, Majik.Core.Stack.Stack?)"/>).
    /// </summary>
    public static Creature Create(Player owner, Majik.Core.Stack.Stack? stack) =>
        Create(owner, stack, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Nimble Obstructionist, fully wired.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">Live stack — required for the counter effect to
    /// remove the chosen ability. <see langword="null"/> in pure-shape
    /// tests; the counter effect becomes a no-op.</param>
    /// <param name="triggers">When supplied, the on-cycle trigger is
    /// registered so a self-cycle <see cref="CardCycledEvent"/> auto-queues
    /// the counter.</param>
    /// <param name="eventBus">When supplied, the cycling resolve body
    /// publishes <see cref="CardCycledEvent"/> on resolve.</param>
    public static Creature Create(
        Player owner,
        Majik.Core.Stack.Stack? stack,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Bird, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.8 — Flash. CR 702.9 — Flying. KeywordAbility markers.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // "When you cycle this card, counter target activated or triggered
        // ability you don't control." (CR 702.32d / CR 603.6)
        //
        // EventTriggerCondition<CardCycledEvent> gated to:
        //   1. ReferenceEquals(e.Card, card) — the printed self-cycle gate.
        //   2. e.Player == card.Controller   — "you cycle" (CR 109.5).
        // ActiveZones = {Graveyard} because the cycling resolve body
        // discards the card before publishing CardCycledEvent, so the
        // trigger functions from the graveyard (same posture as
        // DecreeOfPainFactory's on-cycle sweep).
        // ----------------------------------------------------------------
        TriggeredAbility? cycleTrigger = null;

        var counterEffect = new Effect(
            $"{CardName} — counter target activated or triggered ability you don't control",
            () =>
            {
                if (cycleTrigger == null || stack == null) return;

                var chosen = cycleTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];

                // CR 608.2b — recheck legality at resolution. Legal target:
                // an activated or triggered ability still on the stack that
                // the cycling player does NOT control. Mana abilities can't
                // appear here (CR 605.1 — they never use the stack), so the
                // "activated or triggered" predicate is satisfied
                // structurally.
                if (raw is not IStackObject obj) return;
                if (raw is not (ITriggeredAbility or IActivatedAbility)) return;

                // "you don't control" — an ability the controller controls
                // is an illegal target (CR 608.2b → no-op).
                if (ReferenceEquals(obj.Controller, card.Controller ?? owner)) return;

                if (!stack.GetAll().Contains(obj)) return;

                // CR 701.5b — countered ability ceases to exist (no
                // graveyard hop for an ability).
                OracleSpellBinder.RemoveFromStack(stack, obj);
            });

        cycleTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardCycledEvent>(
                (e, _) =>
                    ReferenceEquals(e.Card, card)
                    && ReferenceEquals(e.Player, card.Controller ?? owner)),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Graveyard },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target activated or triggered ability you don't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(cycleTrigger);
        triggers?.RegisterTriggeredAbility(cycleTrigger);

        // ----------------------------------------------------------------
        // Cycling {2}{U} — CR 702.32. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}
