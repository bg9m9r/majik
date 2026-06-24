using System;
using System.Collections.Generic;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wildwood Mentor (Bloomburrow, {2}{G}).
///
/// Creature — Treefolk 1/1. Oracle text (verified against Scryfall):
///   "Whenever a token you control enters, put a +1/+1 counter on this
///    creature.
///    Whenever this creature attacks, another target attacking creature gets
///    +X/+X until end of turn, where X is this creature's power."
///
/// Two triggered abilities:
///
/// 1. <b>Token-ETB counter trigger (CR 603.1 / CR 122.1)</b> — a
///    <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> gated on
///    the entering card landing on the battlefield, being a token
///    (<see cref="Permanent.IsToken"/> — the same probe Anointer Priest /
///    Bridge from Below use) and being controlled by Wildwood Mentor's
///    controller ("a token you control" — CR 109.4 / CR 603.6d reads the
///    post-ETB controller). On resolution a single
///    <see cref="CounterType.PlusOnePlusOne"/> counter is put on Wildwood Mentor
///    itself ("this creature"). Mirrors <see cref="AnointerPriestFactory"/>'s
///    creature-token-ETB shape, swapping lifegain for a self-counter (the
///    counter-add shape of <see cref="StensiaMasqueradeFactory"/>).
///
/// 2. <b>Attack trigger — pump another attacking creature (CR 508.1f /
///    CR 601.2c / CR 608.2h)</b> — a <see cref="TriggeredAbility"/> via
///    <see cref="Triggers.OnAttackSelf"/> with a mandatory 1..1
///    <see cref="TargetRequest"/> whose candidate gatherer offers only OTHER
///    creatures the live combat-membership registry reports as attacking
///    (<see cref="Majik.Core.Combat.CombatMembershipRegistryProvider"/>). On
///    resolution, X — Wildwood Mentor's power read AT RESOLUTION (CR 608.2h —
///    the value is locked in as the ability resolves, so accrued +1/+1 counters
///    are reflected) — is applied as a <see cref="PumpUntilEndOfTurnEffect"/>
///    (Layer 7c, +X/+X, expiring at end of turn per CR 514.2). The target is
///    re-checked as still attacking at resolution (CR 608.2b). Same
///    target-an-attacker + pump plumbing as
///    <see cref="RestlessVinestalkFactory"/>, with a dynamic X instead of a
///    fixed set-base.
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> overload attaches both triggers
/// for shape inspection but registers nothing (no trigger manager, no layers
/// service). The <see cref="Create(Player, TriggerManager, ContinuousEffectsService)"/>
/// overload — the live wiring — registers the triggers and routes the attack
/// pump through the supplied <see cref="ContinuousEffectsService"/>.
///
/// ## Deferred (v1 gaps)
/// None — both abilities map onto shipped engine primitives (CardMovedEvent
/// token-ETB trigger, +1/+1 counter add, OnAttackSelf trigger, combat-membership
/// targeting, PumpUntilEndOfTurnEffect).
/// </summary>
[CardName("Wildwood Mentor")]
public static class WildwoodMentorFactory
{
    public const string CardName = "Wildwood Mentor";
    public const string Slug = "wildwood-mentor";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Shape-only constructor — both triggers are attached for inspection but
    /// NOT registered (no trigger manager) and the attack pump records no
    /// continuous effect (no layers service). Suitable for factory-shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, effects: null);

    /// <summary>
    /// Construct Wildwood Mentor. When <paramref name="triggers"/> is supplied
    /// both triggered abilities are registered so a matching
    /// <see cref="CardMovedEvent"/> (token ETB) or
    /// <see cref="CreatureAttacksEvent"/> (this creature attacks) queues them
    /// automatically. When <paramref name="effects"/> is supplied the attack
    /// trigger's +X/+X pump is registered against it (CR 613.7c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager — when supplied both triggers are
    /// registered. May be null (shape tests).</param>
    /// <param name="effects">Continuous-effects service for the attack pump.
    /// May be null — the pump resolves to a no-op (no layers service).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Treefolk subtype, {2}{G}, 1/1). The JSON carries no abilities — the
        // two triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // 1. "Whenever a token you control enters, put a +1/+1 counter on
        //    this creature." — CR 603.1 / CR 122.1.
        //
        // Predicate gates on the entering card landing on the battlefield,
        // being a token (Permanent.IsToken — same probe Anointer Priest
        // uses) and being controlled by Wildwood Mentor's controller. The
        // current Card.Controller is the post-move controller, the correct
        // reading per CR 603.6d (ETB triggers see post-ETB state). On
        // resolution the +1/+1 counter is added to Wildwood Mentor itself.
        // ----------------------------------------------------------------
        var counterCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card is Permanent perm
            && perm.IsToken
            && ReferenceEquals(e.Card.Controller, card.Controller ?? owner));

        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on this creature (a token you control entered)",
            () =>
            {
                // CR 608.2 — re-check Wildwood Mentor is still on the
                // battlefield at resolution; otherwise nothing happens.
                if (card.Zone != ZoneType.Battlefield) return;
                card.Counters.Add(CounterType.PlusOnePlusOne, 1);
            });

        var counterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: counterCondition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(counterTrigger);
        triggers?.RegisterTriggeredAbility(counterTrigger);

        // ----------------------------------------------------------------
        // 2. "Whenever this creature attacks, another target attacking
        //    creature gets +X/+X until end of turn, where X is this
        //    creature's power." — CR 508.1f / CR 601.2c / CR 608.2h.
        //
        // OnAttackSelf fires when Wildwood Mentor is declared as an attacker.
        // A mandatory 1..1 TargetRequest offers only OTHER creatures the live
        // combat-membership registry reports as attacking. On resolution X =
        // Wildwood Mentor's power, read at RESOLUTION (CR 608.2h — so accrued
        // +1/+1 counters count), and the target (re-checked still attacking
        // per CR 608.2b) gets +X/+X until end of turn via a Layer-7c
        // PumpUntilEndOfTurnEffect (expires per CR 514.2).
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;

        var targetRequest = new TargetRequest(
            Description: "another target attacking creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Buff,
            // CR 508 — offer only OTHER creatures the live combat-membership
            // registry reports as attacking right now ("another ... attacking").
            CandidateGatherer: _ => GatherOtherAttackingCreatures(card).Cast<object>().ToList());

        var pumpEffect = new Effect(
            $"{CardName}: another target attacking creature gets +X/+X until end of turn (X = this creature's power)",
            () =>
            {
                if (effects == null) return; // no layers service — shape-only path

                var target = ResolveTargetCreature(attackTrigger, card);
                if (target == null) return; // illegal / removed target → no-op

                // CR 608.2h — X is Wildwood Mentor's power read at resolution
                // (includes accrued +1/+1 counters). CR 613.7c / 514.2 —
                // +X/+X expiring at end of turn.
                int x = card.Power;
                effects.Register(new PumpUntilEndOfTurnEffect(target, x, x));
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { pumpEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 601.2c candidate pool for "another target attacking creature": every
    /// creature the live combat-membership registry reports as attacking,
    /// excluding Wildwood Mentor itself ("another").
    /// </summary>
    private static IReadOnlyList<Creature> GatherOtherAttackingCreatures(Creature self)
    {
        var registry = Majik.Core.Combat.CombatMembershipRegistryProvider.Current;
        return registry.AttackingOrBlocking()
            .OfType<Creature>()
            .Where(c => registry.IsAttacking(c) && !ReferenceEquals(c, self))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// CR 608.2c — read the chosen "another target attacking creature" from the
    /// trigger's <see cref="TriggeredAbility.ChosenTargets"/>. Returns null when
    /// no legal target was chosen, the chosen object is Wildwood Mentor itself
    /// (defensive — "another"), or the target is no longer attacking at
    /// resolution (CR 608.2b).
    /// </summary>
    private static Creature? ResolveTargetCreature(TriggeredAbility? trigger, Creature self)
    {
        if (trigger is null
            || trigger.ChosenTargets.Count == 0
            || trigger.ChosenTargets[0].Count == 0)
        {
            return null;
        }

        if (trigger.ChosenTargets[0][0] is not Creature chosen) return null;
        if (ReferenceEquals(chosen, self)) return null;
        // CR 608.2b — the target must still be attacking at resolution.
        if (!Majik.Core.Combat.CombatMembershipRegistryProvider.Current.IsAttacking(chosen))
        {
            return null;
        }
        return chosen;
    }
}
