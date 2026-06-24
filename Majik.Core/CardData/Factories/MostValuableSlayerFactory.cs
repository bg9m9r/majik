using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Most Valuable Slayer (Murders at Karlov Manor / Tarkir
/// Dragonstorm reprint pool, {3}{R}). Creature — Human Warrior 2/4. Oracle text
/// (verified against Scryfall 2026-06-24):
///   "Whenever you attack, target attacking creature gets +1/+0 and gains first
///    strike until end of turn."
///
/// The base shape (name, Creature, Human + Warrior subtypes, {3}{R}, 2/4) is
/// materialised from the embedded JSON definition (<c>most-valuable-slayer.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The "whenever you attack" trigger
/// is layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// attack triggers, until-end-of-turn pumps, or keyword grants (same posture as
/// <see cref="IntiSeneschalOfTheSunFactory"/> / <see cref="EnduringCourageFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Attack trigger (CR 508.1 / 603.1)</b> — fires on
///   <see cref="AttackersDeclaredEvent"/> when this card's controller is the
///   attacking player ("Whenever you attack" — CR 508.1 / 109.5). The triggering
///   <see cref="Majik.Core.Combat.Combat"/> is captured off the matched event
///   (CR 603.2 — the ability is associated with the event that triggered it) so
///   the resolve body can read the declared attackers.
/// - <b>"Target attacking creature gets +1/+0 and gains first strike until end of
///   turn." (CR 613.7c Layer 7c + CR 613.1c Layer 6 / CR 514.2)</b> — on
///   resolution the chosen attacking creature gets a
///   <see cref="PumpUntilEndOfTurnEffect"/> (+1/+0) and a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> ("First Strike"), both
///   registered on the game-wide <see cref="ContinuousEffectsService"/> and both
///   self-expiring in the cleanup step (CR 514.2). Same pump + keyword-grant
///   machinery Enduring Courage applies to its ETB target.
///
/// ## Target resolution
/// The "target attacking creature" is resolved via
/// <paramref name="attackTargetResolver"/> (honouring an explicit
/// <see cref="TriggeredAbility.ChosenTargets"/> pick when the trigger was
/// dispatched with one), defaulting to the first declared attacker the
/// controller controls — the same v1 closure-injection posture as
/// <see cref="IntiSeneschalOfTheSunFactory"/> / <see cref="SoaringThoughtThiefFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Shape-only path</b>: without a live <see cref="ContinuousEffectsService"/>
///   the pump / first-strike grant body no-ops (nothing to register against);
///   without a <see cref="TriggerManager"/> the trigger is attached to the card
///   shape but not auto-registered. The live wire-up site (the effects-aware
///   dispatch overload) supplies the CES, and the engine discovers the attached
///   trigger.
/// - <b>Agent-driven target pick</b>: the buff lands on the resolver-chosen
///   attacker (default: first attacker the controller controls). Full
///   agent-driven "target attacking creature" selection is deferred behind the
///   same queue as Inti / Soaring Thought-Thief.
/// </summary>
[CardName("Most Valuable Slayer")]
public static class MostValuableSlayerFactory
{
    public const string CardName = "Most Valuable Slayer";
    public const string Slug = "most-valuable-slayer";

    /// <summary>Power bonus the target attacking creature gets (+1/+0).</summary>
    public const int PowerBonus = 1;

    /// <summary>Toughness bonus the target attacking creature gets (+1/+0 → none).</summary>
    public const int ToughnessBonus = 0;

    /// <summary>Granted keyword — CR 702.7 First Strike.</summary>
    public const string GrantedFirstStrike = "First Strike";

    /// <summary>
    /// Construct Most Valuable Slayer with no live runtime services. The attack
    /// trigger is attached for shape inspection (no live pump / first-strike
    /// grant — nothing to register against). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to when no CES is supplied.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, continuousEffects: null, triggers: null, attackTargetResolver: null);

    /// <summary>
    /// Production effects-aware overload matched by the source generator's
    /// instance-swap dispatch (<c>NamedCardFactory.CreateGeneratedWithEffects</c>
    /// requires this exact <c>Create(Player, ContinuousEffectsService)</c>
    /// signature). Wires the attack trigger's +1/+0 pump + First Strike grant
    /// against the live game-wide service.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
        => Create(owner, continuousEffects: effects, triggers: null, attackTargetResolver: null);

    /// <summary>
    /// Construct Most Valuable Slayer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Game-wide layers service. When supplied,
    /// the attack trigger's resolution registers the +1/+0 pump + First Strike
    /// grant on the target attacking creature against this service. When null,
    /// the grant body no-ops (shape-only path used by identity / trigger-shape
    /// tests).</param>
    /// <param name="triggers">When supplied, the attack trigger is registered so
    /// a matching <see cref="AttackersDeclaredEvent"/> lands it on the stack
    /// automatically. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="attackTargetResolver">Closure returning the "target attacking
    /// creature" given the live <see cref="Majik.Core.Combat.Combat"/>. May be
    /// null — defaults to the first declared attacker the controller controls.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        Func<Majik.Core.Combat.Combat, Creature?>? attackTargetResolver = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human +
        // Warrior, {3}{R}, 2/4). The JSON carries no abilities — the attack
        // trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Capture the combat from the triggering event so the resolve body can
        // read the declared attackers (CR 603.2 — a triggered ability is
        // associated with the specific event that triggered it).
        Majik.Core.Combat.Combat? capturedCombat = null;

        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
        {
            // "Whenever you attack" — only when this card's controller is the
            // attacking player (CR 508.1 / 109.5).
            if (!ReferenceEquals(e.Combat.AttackingPlayer, card.Controller ?? owner))
                return false;
            capturedCombat = e.Combat;
            return true;
        });

        TriggeredAbility? attackTrigger = null;
        var pumpEffect = new Effect(
            $"{CardName}: target attacking creature gets +{PowerBonus}/+{ToughnessBonus} and gains first strike until end of turn",
            () =>
            {
                var combat = capturedCombat;
                capturedCombat = null;
                ResolveAttackTrigger(attackTrigger, combat, card, owner, continuousEffects, attackTargetResolver);
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { pumpEffect },
            // CR 113.6 — Most Valuable Slayer's attack trigger functions only
            // from the battlefield.
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target attacking creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    private static void ResolveAttackTrigger(
        TriggeredAbility? attackTrigger,
        Majik.Core.Combat.Combat? combat,
        Creature card,
        Player owner,
        ContinuousEffectsService? effects,
        Func<Majik.Core.Combat.Combat, Creature?>? attackTargetResolver)
    {
        if (effects == null) return; // shape-only path — nothing to register.
        if (combat == null) return;
        var controller = card.Controller ?? owner;

        var target = ResolveTarget(attackTrigger, combat, controller, attackTargetResolver);
        if (target == null) return;

        // The pump (layer 7c) and First Strike (layer 6) are both read off the
        // target creature's ActiveEffects layers service, so the target must be
        // wired to this game-wide service for the grants to surface. In the live
        // engine every permanent shares the one game CES, so this is normally
        // already the case.
        target.ActiveEffects ??= effects;

        // CR 613.7c (Layer 7c) — +1/+0 until end of turn.
        effects.Register(new PumpUntilEndOfTurnEffect(target, PowerBonus, ToughnessBonus));

        // CR 613.1c (Layer 6) / CR 702.7 — gains First Strike until end of turn.
        effects.Register(new GrantKeywordUntilEndOfTurnEffect(target, GrantedFirstStrike));
    }

    private static Creature? ResolveTarget(
        TriggeredAbility? attackTrigger,
        Majik.Core.Combat.Combat combat,
        Player controller,
        Func<Majik.Core.Combat.Combat, Creature?>? attackTargetResolver)
    {
        // CR 115 — honour an explicit target if the trigger was dispatched with
        // one (ChosenTargets[0][0] is the agent-picked attacking creature).
        if (attackTrigger != null
            && attackTrigger.ChosenTargets.Count > 0
            && attackTrigger.ChosenTargets[0].Count > 0
            && attackTrigger.ChosenTargets[0][0] is Creature chosen)
        {
            return chosen;
        }

        // Injected resolver, else the v1 default.
        return attackTargetResolver?.Invoke(combat)
            ?? DefaultAttackTarget(combat, controller);
    }

    private static Creature? DefaultAttackTarget(Majik.Core.Combat.Combat combat, Player controller)
    {
        // v1 fallback "target attacking creature" — first declared attacker the
        // controller controls (same closure-injection posture as Inti, Seneschal
        // of the Sun's reflexive +1/+1-counter target).
        foreach (var atk in combat.Attackers)
        {
            // CR 508 — Attacker.Creature is Permanent-typed (animated manlands
            // may attack); this v1 fallback targets a real attacking CREATURE
            // card, so an animated-land attacker is skipped here.
            if (atk?.Creature is not Creature creature) continue;
            if (ReferenceEquals(creature.Controller, controller)) return creature;
        }
        return null;
    }
}
