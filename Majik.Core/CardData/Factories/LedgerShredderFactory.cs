using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ledger Shredder (Streets of New Capenna, {1}{U}).
///
/// Creature — Bird Advisor 1/3. Oracle text:
///   "Flying.
///    Whenever you cast the second spell each turn, surveil 1.
///    Whenever Ledger Shredder surveils, put a +1/+1 counter on it."
///
/// ## Implemented (v1)
/// - 1/3 Bird Advisor with Flying (<see cref="KeywordAbility"/>).
/// - Two triggered abilities, both wired through the trigger pipeline:
///     1. SpellCast watcher — fires on the controller's 2nd cast each turn
///        (CR 603.2). Effect = surveil 1 (CR 701.42), routed through
///        <see cref="Majik.Core.Primitives.Fx.Surveil"/> so a
///        <see cref="SurveilEvent"/> is published on the bus.
///     2. Self-surveil reaction — proper
///        <see cref="Triggers.OnSurveil"/> condition over
///        <see cref="SurveilEvent"/>. Effect = put a +1/+1 counter on it
///        (CR 122). Picks up surveils from any source (Consider, Dragon's
///        Rage Channeler, etc.), not just Ledger Shredder's own.
/// - Per-turn count is held in a closure private to this card instance and
///   reset by a <see cref="TurnStartedEvent"/> subscription when an event
///   bus is supplied (CR 500.1).
/// - Surveil decision routes through the registered
///   <see cref="IPlayerAgent.ChooseSurveilDecisionAsync"/> when one is
///   bound to the controller; otherwise falls back to all-to-graveyard
///   (matches Library Surveyor / Underground Mortuary v1 behavior).
///
/// ## Deferred (v1 gaps)
/// - Cast-counting predicate increments on every <see cref="SpellCastEvent"/>
///   for the controller — including Ledger Shredder's own cast (a creature
///   spell counts as a spell, per CR 700.2). That's correct for the
///   second-spell-each-turn rider but means callers exercising the trigger
///   manually must publish a SpellCastEvent for the first spell too. Tests
///   below do this.
/// </summary>
[CardName("Ledger Shredder")]
public static class LedgerShredderFactory
{
    /// <summary>
    /// Construct Ledger Shredder with no live bus / trigger-manager
    /// wiring. Both triggered abilities are attached to the card so
    /// structural tests can observe their shape; the per-turn count is
    /// held in a closure but is never reset (callers exercising the
    /// trigger manually can reset by constructing a fresh card or by
    /// invoking the (owner, bus, triggers) overload).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Ledger Shredder with optional event bus + trigger
    /// manager. When <paramref name="eventBus"/> is supplied, a
    /// <see cref="TurnStartedEvent"/> handler resets the per-turn cast
    /// count (CR 500.1). When <paramref name="triggers"/> is supplied,
    /// both triggered abilities are registered so the bus surfaces them
    /// as pending. When <paramref name="replacements"/> is supplied, the
    /// surveil-self +1/+1 counter placement is routed through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season replacements can rewrite the count (CR 614).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Ledger Shredder",
            manaCost: "{1}{U}",
            power: 1,
            toughness: 3,
            subtypes: new[] { CardSubtype.Bird, CardSubtype.Advisor });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Per-turn spell-cast count. Closure shared between the trigger
        // predicate and the TurnStartedEvent reset handler.
        // ----------------------------------------------------------------
        var spellsCastThisTurn = new int[] { 0 };

        // Trigger 2: "Whenever Ledger Shredder surveils, put a +1/+1
        // counter on it." CR 122.1 — counters are placed on the permanent
        // in real time. Surveil event fires for any of the controller's
        // surveils (CR 701.42); since Ledger Shredder's own surveil
        // trigger surveils for `owner`, this condition catches it.
        var counterEffect = new Effect(
            "Ledger Shredder: put a +1/+1 counter on it (whenever it surveils)",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements));

        var surveilSelfTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnSurveil(owner),
            effects: new IEffect[] { counterEffect });

        // Trigger 1: "Whenever you cast the second spell each turn,
        // surveil 1." Predicate increments the per-turn count on every
        // SpellCastEvent owned by the controller and only matches on the
        // exact transition to 2 (CR 603.2 / 603.3 — trigger only fires
        // when its condition becomes true). Casts beyond the second do
        // not retrigger.
        var secondSpellCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Spell.Controller, owner)) return false;
            spellsCastThisTurn[0]++;
            return spellsCastThisTurn[0] == 2;
        });

        var surveilEffect = new Effect(
            "Ledger Shredder: surveil 1 (whenever you cast your second spell each turn)",
            async ctx =>
            {
                var peeked = Majik.Core.Keywords.SurveilAction.Peek(owner, 1);
                if (peeked.Count == 0)
                {
                    // Empty library — the surveil attempt still counts
                    // as a surveil event per CR 701.42a. Publish so the
                    // self-trigger fires; no library mutation needed.
                    eventBus?.Publish(new SurveilEvent(owner, 1, Array.Empty<ICard>()));
                    return;
                }

                var agent = ctx.Agent ?? AgentRegistry.Get(owner);
                Majik.Core.Keywords.SurveilAction.SurveilDecision decision;
                if (agent != null)
                {
                    decision = (await agent.ChooseSurveilDecisionAsync( ctx.Game, peeked).ConfigureAwait(false));
                }
                else
                {
                    decision = new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                        ToGraveyard: peeked.ToList(),
                        TopOrder: Array.Empty<Majik.Core.Cards.ICard>());
                }

                // Fx.Surveil applies the decision AND publishes
                // SurveilEvent on the supplied bus — the self-trigger
                // (Triggers.OnSurveil) picks it up via the trigger
                // pipeline so the +1/+1 counter chains naturally.
                Majik.Core.Primitives.Fx.Surveil(owner, 1, decision, eventBus);
            });

        var secondSpellTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: secondSpellCondition,
            effects: new IEffect[] { surveilEffect });

        card.AddAbility(secondSpellTrigger);
        card.AddAbility(surveilSelfTrigger);

        // CR 500.1 — reset the per-turn count when a new turn starts.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => spellsCastThisTurn[0] = 0);
        }

        // Live trigger registration. surveilSelfTrigger now uses a real
        // EventTriggerCondition<SurveilEvent> so the bus surfaces it as
        // pending when Fx.Surveil publishes (or any other surveil source).
        triggers?.RegisterTriggeredAbility(secondSpellTrigger);
        triggers?.RegisterTriggeredAbility(surveilSelfTrigger);

        return card;
    }
}
