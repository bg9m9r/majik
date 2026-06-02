using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inferno Titan (Magic 2011 / Modern staple,
/// {4}{R}{R}). Creature — Giant, 6/6.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{R}: This creature gets +1/+0 until end of turn.
///    Whenever this creature enters or attacks, it deals 3 damage divided as
///    you choose among one, two, or three targets."
///
/// ## Implemented (v1)
///
/// - 6/6 Creature — Giant, mana cost {4}{R}{R}, owner/controller wired. Base
///   shape materialised from the embedded JSON definition
///   (<c>inferno-titan.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (same posture as
///   <see cref="GraveTitanFactory"/>).
/// - <b>{R}: +1/+0 until end of turn</b> (firebreathing, CR 602 /
///   CR 613.1f Layer 7c). Wired as an <see cref="ActivatedAbility"/> with a
///   single <see cref="ManaCostCost"/> of <c>{R}</c> and no targets — the
///   ability pumps Inferno Titan itself. On resolution it registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, 0) against
///   <see cref="Creature.ActiveEffects"/>. When <c>ActiveEffects</c> is null
///   (shape-only test path) the pump silently no-ops — identical shape to
///   <see cref="WallOfFireFactory"/>. Repeatable (no once-per-turn clause):
///   each {R} paid stacks an additional +1/+0 for the turn (CR 613.1f).
/// - <b>"Whenever this creature enters or attacks, it deals 3 damage divided
///   as you choose among one, two, or three targets."</b> The single printed
///   ability has two trigger conditions joined by "or" (CR 603.1) — modelled
///   here as TWO <see cref="TriggeredAbility"/> instances sharing the same
///   divided-damage effect body, because the engine keys one event-typed
///   condition per trigger (same modelling choice as
///   <see cref="GraveTitanFactory"/>):
///   - the ETB half on <see cref="CardMovedEvent"/> → Battlefield matching
///     Inferno Titan itself (<see cref="Triggers.OnEnterBattlefieldSelf"/>,
///     CR 603.6a), and
///   - the attack half on <see cref="CreatureAttacksEvent"/> matching Inferno
///     Titan itself (<see cref="Triggers.OnAttackSelf"/>, CR 508.1f).
///   Each resolves by dealing 3 damage divided among 1..3 targets
///   (CR 601.2d division / CR 119.4 — the full 3 must be assigned, ≥1 per
///   chosen target) via <see cref="DealDividedDamage"/>, routing each
///   allocation through <see cref="Fx.DealDamageAny"/> (Player / Creature /
///   Planeswalker — CR 119 / CR 306.7), mirroring
///   <see cref="ShatterskullSmashingFactory"/>'s divided-damage primitive.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real divide-damage prompt</b>: CR 601.2d announces the damage
///   division as the triggered ability is put on the stack; the engine has no
///   agent-driven division prompt yet. <see cref="DealDividedDamage"/> takes a
///   caller-supplied <c>distribute</c> strategy (defaulting to
///   <see cref="DefaultAllocation"/>) as the stand-in — same posture as
///   <see cref="ShatterskullSmashingFactory"/> / <see cref="ForkedBoltFactory"/>.
///   When no targets are supplied (shape-only test path, no live combat /
///   stack), the trigger body is a no-op (CR 608.2b — no legal targets = the
///   ability does nothing).
/// - <b>Trigger-on-stack timing</b>: the effect body runs immediately when the
///   trigger executes rather than waiting on the stack (APNAP). Same v1
///   collapse as <see cref="GraveTitanFactory"/>.
/// </summary>
[CardName("Inferno Titan")]
public static class InfernoTitanFactory
{
    public const string CardName = "Inferno Titan";
    public const string Slug = "inferno-titan";
    public const string FirebreathingCost = "{R}";
    public const int DamageTotal = 3;

    /// <summary>
    /// Construct Inferno Titan with no live runtime services. Suitable for
    /// card-shape / dispatcher tests — both ETB/attack triggers and the
    /// firebreathing ability are attached to the card shape, but the triggers
    /// are not registered with any <see cref="TriggerManager"/>. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, distribute: null);

    /// <summary>
    /// Construct a fully-wired Inferno Titan.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the ETB and attack
    /// triggers against. May be null — both triggers are still attached to the
    /// card shape.</param>
    /// <param name="distribute">Optional damage-division strategy used by both
    /// triggers. Receives (legalTargets, total=3) and returns the per-target
    /// allocation (sum reconciled to 3 via <see cref="NormalizeAllocation"/>).
    /// When null, <see cref="DefaultAllocation"/> is used. The trigger bodies
    /// supply an empty target list in the shape-only path, so with no live
    /// targeting the triggers are a no-op (CR 608.2b).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<object>, int, IReadOnlyDictionary<object, int>>? distribute = null)
    {
        System.ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature type,
        // Giant subtype, {4}{R}{R}, 6/6). Firebreathing + the damage triggers
        // are layered on below — none is expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {R}: This creature gets +1/+0 until end of turn. (CR 602)
        //
        // Plain activated ability (uses the stack per CR 605.1 / CR 602.2;
        // produces no mana so NOT a mana ability). No target — pumps Inferno
        // Titan itself. On resolution a PumpUntilEndOfTurnEffect(+1, 0) is
        // registered against card.ActiveEffects (Layer 7c, CR 613.1f). When
        // ActiveEffects is null (shape-only path) the pump no-ops, identical
        // to WallOfFireFactory. Repeatable — no once-per-turn restriction.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: +1/+0 until end of turn ({{R}} firebreathing)",
            () => card.ActiveEffects?.Register(
                new PumpUntilEndOfTurnEffect(card, 1, 0)));

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(FirebreathingCost) },
            effects: new IEffect[] { pumpEffect }));

        // ----------------------------------------------------------------
        // "Whenever this creature enters or attacks, it deals 3 damage divided
        // as you choose among one, two, or three targets." (CR 603.1.)
        // Modelled as two triggers sharing one effect body — the engine keys a
        // trigger on a single event type, and "enters" (CardMovedEvent) and
        // "attacks" (CreatureAttacksEvent) are distinct event paths. Each path
        // deals 3 damage; neither fires for the other's event.
        // ----------------------------------------------------------------

        // ETB half — CR 603.6a. Self-entering the battlefield.
        var etbEffect = new Effect(
            $"{CardName}: on enter, deal {DamageTotal} damage divided among 1..3 targets",
            () => DealDividedDamage(System.Array.Empty<object>(), distribute));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Attack half — CR 508.1f self-match.
        var attackEffect = new Effect(
            $"{CardName}: on attack, deal {DamageTotal} damage divided among 1..3 targets",
            () => DealDividedDamage(System.Array.Empty<object>(), distribute));

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 601.2d / CR 119.4 — deal <see cref="DamageTotal"/> (3) damage divided
    /// among <paramref name="targets"/> (any target: Player / Creature /
    /// Planeswalker). The per-target allocation comes from
    /// <paramref name="distribute"/> (defaulting to
    /// <see cref="DefaultAllocation"/>) and is reconciled to sum to exactly 3
    /// via <see cref="NormalizeAllocation"/>. Each allocation is applied via
    /// <see cref="Fx.DealDamageAny"/> (CR 119 / CR 306.7). Empty target list =
    /// no-op (CR 608.2b — no legal targets, the ability does nothing).
    /// </summary>
    public static void DealDividedDamage(
        IReadOnlyList<object> targets,
        Func<IReadOnlyList<object>, int, IReadOnlyDictionary<object, int>>? distribute)
    {
        System.ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0) return;

        var allocation = distribute != null
            ? NormalizeAllocation(distribute(targets, DamageTotal), targets, DamageTotal)
            : DefaultAllocation(targets, DamageTotal);

        foreach (var (target, amount) in allocation)
        {
            if (amount <= 0) continue;
            Fx.DealDamageAny(target, amount);
        }
    }

    /// <summary>
    /// Default damage division — spreads <paramref name="total"/> across
    /// <paramref name="targets"/> as evenly as possible with the remainder
    /// front-loaded, guaranteeing ≥1 per chosen target when
    /// <c>targets.Count ≤ total</c> (CR 601.2d — you must assign at least 1 to
    /// each target you choose). For Inferno Titan (total = 3): 1 target → 3;
    /// 2 targets → 2,1; 3 targets → 1,1,1.
    /// </summary>
    public static IReadOnlyDictionary<object, int> DefaultAllocation(
        IReadOnlyList<object> targets,
        int total)
    {
        System.ArgumentNullException.ThrowIfNull(targets);
        var result = new Dictionary<object, int>();
        if (targets.Count == 0) return result;

        var baseShare = total / targets.Count;
        var remainder = total % targets.Count;

        for (int i = 0; i < targets.Count; i++)
        {
            // Front-load the remainder so the sum is exactly `total`.
            result[targets[i]] = baseShare + (i < remainder ? 1 : 0);
        }
        return result;
    }

    /// <summary>
    /// CR 119.4 — the assigned damage MUST sum to exactly
    /// <paramref name="total"/>. Any over/underflow from the caller-supplied
    /// <paramref name="raw"/> delegate is reconciled onto the first target.
    /// </summary>
    private static IReadOnlyDictionary<object, int> NormalizeAllocation(
        IReadOnlyDictionary<object, int>? raw,
        IReadOnlyList<object> targets,
        int total)
    {
        var result = new Dictionary<object, int>();
        if (targets.Count == 0) return result;

        foreach (var t in targets) result[t] = 0;

        if (raw != null)
        {
            foreach (var (target, amount) in raw)
            {
                if (!result.ContainsKey(target)) continue;
                if (amount < 0) continue;
                result[target] = amount;
            }
        }

        var sum = result.Values.Sum();
        var delta = total - sum;
        if (delta != 0)
        {
            var first = targets[0];
            result[first] = System.Math.Max(0, result[first] + delta);
        }
        return result;
    }
}
