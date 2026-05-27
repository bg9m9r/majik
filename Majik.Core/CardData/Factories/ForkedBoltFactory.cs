using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Forked Bolt (Rise of the Eldrazi / Modern
/// Masters / Modern Horizons 2, {R}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Forked Bolt deals 2 damage divided as you choose among one or two
///    target creatures and/or players."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {R}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..2 "target
///   creature and/or player" request (divided damage — CR 601.2d /
///   CR 119.4).
/// - <b>Division</b> — caller supplies a <c>distribute</c> delegate
///   that, given the legal-at-resolution target list, returns the
///   per-target damage allocation. The sum is clamped to
///   <see cref="TotalDamage"/> = 2 and any over/underflow is corrected:
///   excess damage drops to the first target, missing damage is filled
///   in on the first target as well so the total dealt always equals
///   2 when at least one legal target remains (CR 119.4 — "divide
///   exactly N damage"). The default fallback when no delegate is
///   supplied:
///     * one legal target → 2 damage to that target;
///     * two legal targets → 1 damage to each.
///   Mirrors <see cref="FuryFactory"/>'s caller-supplied-distribute
///   posture pending agent-driven division prompts.
/// - <b>Illegal targets at resolution</b> — filtered out per CR 608.2b.
///   The spell still resolves with whatever legal subset remains; if
///   every target became illegal, the spell does nothing.
///
/// ## Deferred (v1 gaps)
/// - <b>Real divide-damage prompt</b>: CR 601.2d announces the damage
///   division at cast time. The engine has no agent-driven division
///   prompt yet — the <c>distribute</c> Func is the stand-in until
///   that ships (same posture as <see cref="FuryFactory"/>).
/// </summary>
[CardName("Forked Bolt")]
public static class ForkedBoltFactory
{
    public const string CardName = "Forked Bolt";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119.4 — total damage divided across chosen targets.</summary>
    public const int TotalDamage = 2;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/> (the runtime
    /// needs the caller's target resolver, which lives on the
    /// <see cref="GameContext"/>).</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Forked Bolt is
    /// cast. 1..2 "target creature and/or player" request; on
    /// resolution the
    /// <paramref name="distribute"/> delegate (or the default
    /// fallback) splits <see cref="TotalDamage"/> = 2 across the
    /// legal targets, routed through
    /// <see cref="Fx.DealDamageAny"/> (CR 306.7 — planeswalker damage
    /// becomes loyalty removal). "Any target" semantics are restricted
    /// here to creatures + players per the printed wording (no
    /// planeswalker/battle targets despite the post-MH3
    /// rules-text upgrade some red burn spells received — Forked
    /// Bolt's printed text predates planeswalkers as "any target"
    /// recipients).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="distribute">Optional per-target allocation strategy
    /// — given the list of legal-at-resolution targets, return a
    /// dictionary mapping target → damage. Sum must equal 2; any
    /// over/underflow is corrected onto the first target. When null,
    /// the default fallback splits as "all 2 to a single target" or
    /// "1 each across two targets".</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        Func<IReadOnlyList<object>, IReadOnlyDictionary<object, int>>? distribute = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature and/or player",
                    MinTargets: 1,
                    MaxTargets: 2,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var rawTargets = chosen.Targets[0];
                return new IEffect[]
                {
                    Fx.Inline("Forked Bolt: divide 2 damage among up to two targets", () =>
                    {
                        // Resolve all chosen target tokens, dropping
                        // illegal-at-resolution picks (CR 608.2b).
                        var legal = new List<object>();
                        foreach (var token in rawTargets)
                        {
                            var live = resolver(token);
                            if (IsLegalForkedBoltTarget(live))
                            {
                                legal.Add(live);
                            }
                        }
                        if (legal.Count == 0) return; // all targets illegal — fizzle

                        var allocation = distribute != null
                            ? NormalizeAllocation(distribute(legal), legal)
                            : DefaultAllocation(legal);

                        foreach (var (target, amount) in allocation)
                        {
                            if (amount <= 0) continue;
                            Fx.DealDamageAny(target, amount);
                        }
                    }),
                };
            });
    }

    /// <summary>
    /// Default damage division — mirrors how a player would split when
    /// no division strategy is supplied. With 1 legal target → all 2
    /// damage on it; with 2 legal targets → 1 damage each. Returns the
    /// allocation as a dictionary keyed by the live target object.
    /// </summary>
    public static IReadOnlyDictionary<object, int> DefaultAllocation(IReadOnlyList<object> legal)
    {
        ArgumentNullException.ThrowIfNull(legal);
        if (legal.Count == 0) return new Dictionary<object, int>();
        if (legal.Count == 1) return new Dictionary<object, int> { [legal[0]] = TotalDamage };
        // 2 legal targets → 1 + 1 split.
        return new Dictionary<object, int>
        {
            [legal[0]] = 1,
            [legal[1]] = 1,
        };
    }

    /// <summary>
    /// CR 119.4 — the printed damage MUST sum to exactly
    /// <see cref="TotalDamage"/>. Defensive normalisation: any over/
    /// underflow caused by an ill-formed caller-supplied delegate is
    /// reconciled onto the first legal target so the engine never deals
    /// 0 or 3+ damage from Forked Bolt.
    /// </summary>
    private static IReadOnlyDictionary<object, int> NormalizeAllocation(
        IReadOnlyDictionary<object, int>? raw,
        IReadOnlyList<object> legal)
    {
        var result = new Dictionary<object, int>();
        if (legal.Count == 0) return result;

        // Seed with legal targets at 0 so the dictionary covers all
        // entries even when the caller-supplied delegate omits one.
        foreach (var t in legal) result[t] = 0;

        if (raw != null)
        {
            foreach (var (target, amount) in raw)
            {
                if (!result.ContainsKey(target)) continue; // ignore unknown targets
                if (amount < 0) continue;
                result[target] = amount;
            }
        }

        var total = 0;
        foreach (var amount in result.Values) total += amount;
        var delta = TotalDamage - total;
        if (delta != 0)
        {
            // Add (or subtract) the delta on the first legal target so
            // the total normalises to exactly 2. Clamp at 0 to prevent
            // negative damage on a pathological over-allocation.
            var first = legal[0];
            result[first] = Math.Max(0, result[first] + delta);
        }
        return result;
    }

    /// <summary>
    /// CR 115.4 / Forked Bolt's printed text: legal targets are
    /// creatures + players only (no planeswalkers or battles).
    /// </summary>
    private static bool IsLegalForkedBoltTarget(object live)
    {
        if (live is Player) return true;
        if (live is Creature) return true;
        return false;
    }
}
