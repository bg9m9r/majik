using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lightning Axe (Time Spiral / many reprints, {R}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, discard a card or pay {5}.
///    Lightning Axe deals 5 damage to target creature."
///
/// ## Why it gets its own factory
/// Lightning Axe is the disjunctive-additional-cost cousin of
/// <see cref="BombardFactory"/> (4-to-creature at {2}{R}) and
/// <see cref="LavaAxeFactory"/> (5 damage). The 5-to-creature payload is
/// the simple burn shape; the upgrade — and the reason it can't fall to a
/// template binder — is the printed additional cost (CR 601.2f): discard a
/// card OR pay {5}. {R} for five damage to a creature is one of Modern's
/// most efficient creature-removal spells and a premier madness enabler.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {R}.
/// - Additional cost (CR 601.2f):
///   <see cref="DiscardACardOrPayManaAdditionalCost"/> — disjunctive
///   payment that prefers discarding a card when one is available and
///   falls back to paying {5} otherwise. The cast flow's pre-check
///   (<see cref="SpellCastFlow"/>) rejects the cast when NEITHER mode is
///   payable (CR 601.2g — additional cost that can't be paid → cast is
///   illegal). Same posture as <see cref="BoneShardsFactory"/> /
///   Bitter Triumph.
/// - <b>5 damage to target creature</b> —
///   <see cref="BuildSpellDefinition"/> declares a single 1..1
///   "target creature" <see cref="TargetRequest"/>; on resolution deals
///   <see cref="Damage"/> (5) damage to the chosen target through
///   <see cref="Fx.DealDamageAny"/>. A non-creature resolved target is a
///   no-op (CR 608.2b).
///
/// ## Deferred (v1 gaps)
/// - <b>Mode prompt</b>: the agent doesn't choose between discard and
///   pay-{5} at announcement; the cost defaults to discarding when a card
///   is available, otherwise pays the mana. Same queue as
///   <see cref="DiscardACardCost"/>'s deferred discard-target prompt.
/// </summary>
[CardName("Lightning Axe")]
public static class LightningAxeFactory
{
    public const string CardName = "Lightning Axe";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 5;

    /// <summary>CardDef DSL — card shape only. Damage body + additional
    /// cost are supplied at cast time via
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Lightning Axe is
    /// cast. Declares the disjunctive discard-or-pay-{5} additional cost
    /// (CR 601.2f) alongside a single 1..1 "target creature"
    /// <see cref="TargetRequest"/>; on resolution deals <see cref="Damage"/>
    /// (5) damage through <see cref="Fx.DealDamageAny"/> if the target is a
    /// creature; no-ops otherwise (CR 608.2b).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Lightning Axe: 5 damage to target creature", () =>
                    {
                        // CR 608.2b — illegal (non-creature) resolved target → no-op.
                        if (target is not Creature) return;
                        Fx.DealDamageAny(target, Damage);
                    }),
                };
            },
            AdditionalCosts: new IAdditionalCost[]
            {
                new DiscardACardOrPayManaAdditionalCost(),
            });
    }
}
