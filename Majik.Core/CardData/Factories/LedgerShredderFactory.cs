using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

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
/// - Two triggered abilities surfaced on the card so structural shape
///   tests can observe both oracle clauses:
///     1. SpellCast watcher — fires on the controller's 2nd cast each turn
///        (CR 603.2). Effect = surveil 1 (CR 701.42).
///     2. Self-surveil reaction — fires when Ledger Shredder surveils.
///        Effect = put a +1/+1 counter on it (CR 122).
/// - Per-turn count is held in a closure private to this card instance and
///   reset by a <see cref="TurnStartedEvent"/> subscription when an event
///   bus is supplied (CR 500.1).
/// - No <c>SurveilEvent</c> exists in the engine yet, so trigger 2 chains
///   off trigger 1's effect directly: after the surveil decision is
///   applied, the counter effect runs immediately. Both abilities are
///   still present on the card so the oracle shape is preserved.
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
/// - The "Whenever Ledger Shredder surveils" trigger is currently chained
///   inside the effect rather than via the trigger pipeline because there
///   is no SurveilEvent on the bus. Once a SurveilEvent ships, the second
///   trigger can move to a proper <see cref="EventTriggerCondition{T}"/>
///   and pick up surveils from sources other than Ledger Shredder itself
///   (relevant if Ledger Shredder is ever in a deck with extra-surveil
///   replacement effects — none exist as of CR 2025-11-14).
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
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Ledger Shredder with optional event bus + trigger
    /// manager. When <paramref name="eventBus"/> is supplied, a
    /// <see cref="TurnStartedEvent"/> handler resets the per-turn cast
    /// count (CR 500.1). When <paramref name="triggers"/> is supplied,
    /// both triggered abilities are registered so the bus surfaces them
    /// as pending.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
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
        // counter on it." Built first so trigger 1's effect can call its
        // single effect directly when a surveil resolves on this card.
        // CR 122.1 — counters are placed on the permanent in real time.
        var counterEffect = new Effect(
            "Ledger Shredder: put a +1/+1 counter on it (whenever it surveils)",
            () => card.Counters.Add(CounterType.PlusOnePlusOne, 1));

        // Trigger 2 surfaces on the card as a shape-only ability — its
        // condition matches no real event, since there is no SurveilEvent
        // on the bus. The actual mechanic runs via the counterEffect
        // closure invoked from trigger 1's effect.
        var surveilSelfTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<GameEvent>((_, _) => false),
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
            () =>
            {
                var peeked = Majik.Core.Keywords.SurveilAction.Peek(owner, 1);
                if (peeked.Count != 0)
                {
                    var agent = AgentRegistry.Get(owner);
                    Majik.Core.Keywords.SurveilAction.SurveilDecision decision;
                    if (agent != null)
                    {
                        decision = agent.ChooseSurveilDecisionAsync(null, peeked)
                            .GetAwaiter().GetResult();
                    }
                    else
                    {
                        decision = new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                            ToGraveyard: peeked.ToList(),
                            TopOrder: Array.Empty<Majik.Core.Cards.ICard>());
                    }
                    Majik.Core.Keywords.SurveilAction.Apply(owner, 1, decision);
                }

                // CR 603.2 — chain "whenever Ledger Shredder surveils" off
                // the surveil action. Until a SurveilEvent exists on the
                // bus, the counter trigger fires inline.
                counterEffect.Execute();
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

        // Live trigger registration. surveilSelfTrigger has a never-matching
        // condition; registering it is harmless (it never fires) and keeps
        // ability-shape observability consistent.
        triggers?.RegisterTriggeredAbility(secondSpellTrigger);
        triggers?.RegisterTriggeredAbility(surveilSelfTrigger);

        return card;
    }
}
