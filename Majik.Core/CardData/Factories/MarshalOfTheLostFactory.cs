using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Marshal of the Lost (Tarkir: Dragonstorm,
/// {2}{W}{B}). Creature — Orc Warrior 3/3. Oracle text (verified against
/// Scryfall):
///   "Deathtouch
///    Whenever you attack, target creature gets +X/+X until end of turn,
///    where X is the number of attacking creatures."
///
/// The base shape (name, Creature, Orc + Warrior subtypes, {2}{W}{B}, 3/3,
/// Deathtouch keyword) is materialised from the embedded JSON definition
/// (<c>marshal-of-the-lost.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — Deathtouch (CR 702.2) is a
/// printed keyword line, so it comes free off the JSON <c>keywords</c> array
/// as a <see cref="KeywordAbility"/>. Only the attack trigger is layered on
/// here (the JSON <c>AbilityDefinition</c> schema doesn't yet express attack
/// triggers, target creatures, or attacker-count-scaled pumps).
///
/// ## Implemented (v1)
///
/// - <b>Deathtouch</b> (CR 702.2) — from the JSON <c>keywords</c> array.
/// - <b>Attack trigger (CR 508.1 / 603.1)</b> — fires on
///   <see cref="AttackersDeclaredEvent"/> when Marshal's controller is the
///   attacking player ("Whenever you attack"; CR 508.1 / 109.5). On resolve
///   it gives the target creature +X/+X until end of turn, where X is the
///   number of attacking creatures (CR 613.4 / 514.2). X is read from the
///   captured <see cref="Combat"/>'s declared-attacker count — counted at
///   resolution, which equals the declare-attackers count for this trigger
///   since attackers are locked in before the trigger resolves (CR 509). The
///   buff is a <see cref="PumpUntilEndOfTurnEffect"/> registered on the
///   target's <see cref="Creature.ActiveEffects"/> (layer 7c).
///
/// ## Source closure injection (v1 posture)
///
/// Same shape as <see cref="IntiSeneschalOfTheSunFactory"/> /
/// <see cref="SoaringThoughtThiefFactory"/> — the engine's
/// trigger-effect closure doesn't yet surface an agent-driven "target
/// creature" pick from inside the resolve body, so the factory accepts a
/// <paramref name="targetResolver"/> closure. The trigger itself carries a
/// 1..1 "target creature" <see cref="TargetRequest"/> for shape/dispatch
/// observability; the resolver feeds the live target on resolve.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent-driven "target creature" pick</b>: the +X/+X lands on the
///   resolver-chosen creature (default: the first attacking creature the
///   controller controls — the same closure-injection posture as Inti's
///   attack-target resolver). "Target creature" can legally be any creature
///   (including the defender's); full agent-driven targeting is deferred
///   behind the same queue as the rest of the attack-trigger family.
/// - <b>Trigger-on-stack timing</b>: in real MTG the trigger goes on the
///   stack and is targeted on resolution; v1 collapses to
///   trigger-resolves-now. X (the attacker count) is captured from the
///   declared-attackers combat, so the count is stable.
/// </summary>
[CardName("Marshal of the Lost")]
public static class MarshalOfTheLostFactory
{
    public const string CardName = "Marshal of the Lost";
    public const string Slug = "marshal-of-the-lost";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Marshal with no live wiring (the shape / dispatcher path).
    /// The attack trigger is attached for shape observability but its pump
    /// body is a no-op without a layers service. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null, targetResolver: null);

    /// <summary>
    /// Construct Marshal with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">ContinuousEffectsService the attack trigger
    /// registers the +X/+X <see cref="PumpUntilEndOfTurnEffect"/> against.
    /// May be null — the pump is then not granted live.</param>
    /// <param name="triggers">TriggerManager the attack trigger is registered
    /// with so it surfaces as pending. May be null — the trigger is still
    /// attached to the card shape.</param>
    /// <param name="targetResolver">Closure returning the "target creature"
    /// for the pump, given the live <see cref="Combat"/>. May be null —
    /// defaults to the first attacking creature the controller controls.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers,
        System.Func<Majik.Core.Combat.Combat, Creature?>? targetResolver = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Orc +
        // Warrior, {2}{W}{B}, 3/3, Deathtouch). The attack trigger is layered
        // on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        AddAttackTrigger(card, owner, effects, targetResolver, triggers);

        return card;
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever you attack, target creature gets +X/+X until
    // end of turn, where X is the number of attacking creatures."
    // (CR 508.1 / 603.1 / 613.4.)
    // -----------------------------------------------------------------------
    private static void AddAttackTrigger(
        Creature card,
        Player owner,
        ContinuousEffectsService? effects,
        System.Func<Majik.Core.Combat.Combat, Creature?>? targetResolver,
        TriggerManager? triggers)
    {
        // Capture the combat from the triggering event so the resolve body can
        // read the declared attackers (CR 603.2 — a triggered ability is
        // associated with the specific event that triggered it).
        Majik.Core.Combat.Combat? capturedCombat = null;

        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
        {
            // "Whenever you attack" — only when Marshal's controller is the
            // attacking player (CR 508.1 / 109.5).
            if (!ReferenceEquals(e.Combat.AttackingPlayer, card.Controller ?? owner))
                return false;
            capturedCombat = e.Combat;
            return true;
        });

        var attackEffect = new Effect(
            $"{CardName}: on attack, target creature gets +X/+X until end of turn (X = number of attacking creatures)",
            () =>
            {
                var combat = capturedCombat;
                capturedCombat = null;
                ResolveAttackTrigger(combat, card, owner, effects, targetResolver);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { attackEffect },
            // CR 113.6 — Marshal's attack trigger functions only from the
            // battlefield.
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);
    }

    private static void ResolveAttackTrigger(
        Majik.Core.Combat.Combat? combat,
        Creature card,
        Player owner,
        ContinuousEffectsService? effects,
        System.Func<Majik.Core.Combat.Combat, Creature?>? targetResolver)
    {
        if (combat == null) return;
        var controller = card.Controller ?? owner;

        // X = "the number of attacking creatures" (CR 613.4). Counted from the
        // captured declared-attackers combat (CR 509 — attackers are locked in
        // before this trigger resolves, so the count is stable).
        var x = combat.Attackers.Count;
        if (x <= 0) return;

        var target = targetResolver?.Invoke(combat)
            ?? DefaultTarget(combat, controller);
        if (target == null) return;

        // CR 613.4 / 514.2 — +X/+X until end of turn (layer 7c, expires in the
        // cleanup step).
        var layers = target.ActiveEffects ?? effects;
        layers?.Register(new PumpUntilEndOfTurnEffect(target, x, x));
    }

    private static Creature? DefaultTarget(Majik.Core.Combat.Combat combat, Player controller)
    {
        // v1 fallback "target creature" — first declared attacker the
        // controller controls (same closure-injection posture as Inti's
        // attack-target resolver).
        foreach (var atk in combat.Attackers)
        {
            // CR 508 — Attacker.Creature is Permanent-typed (animated manlands
            // may attack); this v1 fallback targets a real attacking CREATURE.
            if (atk?.Creature is not Creature creature) continue;
            if (ReferenceEquals(creature.Controller, controller)) return creature;
        }
        return null;
    }
}
