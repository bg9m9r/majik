using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Electrolyze (Guildpact / Modern Masters, {1}{U}{R}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Electrolyze deals 2 damage divided as you choose among one or two
///    targets.
///    Draw a card."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}{R}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..2 "any target"
///   request (divided damage — CR 601.2d / CR 119.4). The damage clause
///   mirrors <see cref="ForkedBoltFactory"/> exactly: a caller-supplied
///   <c>distribute</c> delegate splits <see cref="TotalDamage"/> = 2
///   across the legal-at-resolution targets; absent a delegate the
///   default fallback is "all 2 to one target" or "1 each across two".
///   Allocation is normalised to exactly 2 (CR 119.4) and illegal targets
///   are filtered at resolution (CR 608.2b). Damage is routed through
///   <see cref="Fx.DealDamageAny"/> so planeswalker targets become loyalty
///   removal (CR 306.7) — Electrolyze's modern "any target" templating
///   accepts creatures, players, planeswalkers, and battles.
/// - <b>Draw rider</b> — an unconditional top-of-library draw mirroring
///   <see cref="IzzetCharmFactory"/>'s loot draw. The draw is part of the
///   same resolution and is independent of how many damage targets remain
///   legal (CR 608.2c — the rest of the spell still resolves). An empty
///   library flags <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///   for the SBA loss check (CR 704.5b).
///
/// ## Deferred (v1 gaps)
/// - <b>Real divide-damage prompt</b>: CR 601.2d announces the division at
///   cast time. The engine has no agent-driven division prompt yet — the
///   <c>distribute</c> Func is the stand-in (same posture as
///   <see cref="ForkedBoltFactory"/> / <see cref="FuryFactory"/>).
/// </summary>
[CardName("Electrolyze")]
public static class ElectrolyzeFactory
{
    public const string CardName = "Electrolyze";
    public const string PrintedManaCost = "{1}{U}{R}";

    /// <summary>CR 119.4 — total damage divided across chosen targets.</summary>
    public const int TotalDamage = 2;

    /// <summary>Construct Electrolyze as an Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Electrolyze is
    /// cast. One 1..2 "any target" request; on resolution the
    /// <paramref name="distribute"/> delegate (or the default fallback)
    /// splits <see cref="TotalDamage"/> = 2 across the legal targets via
    /// <see cref="Fx.DealDamageAny"/>, then <paramref name="caster"/>
    /// draws a card unconditionally.
    /// </summary>
    /// <param name="caster">The spell's controller — draws the card.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game object).</param>
    /// <param name="distribute">Optional per-target allocation strategy —
    /// given the list of legal-at-resolution targets, return a dictionary
    /// mapping target → damage. Sum must equal 2; any over/underflow is
    /// corrected onto the first target. When null, the default fallback
    /// splits as "all 2 to a single target" or "1 each across two".</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        Func<IReadOnlyList<object>, IReadOnlyDictionary<object, int>>? distribute = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 2,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            },
            EffectFactory: chosen =>
            {
                var rawTargets = chosen.Targets.Count > 0
                    ? chosen.Targets[0]
                    : Array.Empty<object>();

                return new IEffect[]
                {
                    Fx.Inline("Electrolyze: divide 2 damage among up to two targets, then draw a card", () =>
                    {
                        // Resolve chosen tokens, dropping illegal-at-resolution
                        // picks (CR 608.2b).
                        var legal = new List<object>();
                        foreach (var token in rawTargets)
                        {
                            var live = resolver(token);
                            if (IsLegalTarget(live))
                            {
                                legal.Add(live);
                            }
                        }

                        if (legal.Count > 0)
                        {
                            var allocation = distribute != null
                                ? NormalizeAllocation(distribute(legal), legal)
                                : DefaultAllocation(legal);

                            foreach (var (target, amount) in allocation)
                            {
                                if (amount <= 0) continue;
                                Fx.DealDamageAny(target, amount);
                            }
                        }
                        // CR 608.2c — even if every damage target became
                        // illegal, the rest of the spell still resolves: the
                        // unconditional "Draw a card" rider always fires.

                        // CR 121.1 — top-of-library draw. Empty library flags
                        // the player for the SBA loss check (CR 704.5b),
                        // mirroring IzzetCharmFactory.
                        var top = caster.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            caster.MarkTriedToDrawFromEmptyLibrary();
                        }
                        else
                        {
                            caster.Zones.Library.RemoveCard(top);
                            caster.Zones.Hand.AddCard(top);
                            top.SetZone(ZoneType.Hand);
                        }
                    }),
                };
            });
    }

    /// <summary>
    /// Default damage division — with 1 legal target → all 2 damage on it;
    /// with 2 legal targets → 1 damage each (CR 119.4). Mirrors
    /// <see cref="ForkedBoltFactory.DefaultAllocation"/>.
    /// </summary>
    public static IReadOnlyDictionary<object, int> DefaultAllocation(IReadOnlyList<object> legal)
    {
        ArgumentNullException.ThrowIfNull(legal);
        if (legal.Count == 0) return new Dictionary<object, int>();
        if (legal.Count == 1) return new Dictionary<object, int> { [legal[0]] = TotalDamage };
        return new Dictionary<object, int>
        {
            [legal[0]] = 1,
            [legal[1]] = 1,
        };
    }

    /// <summary>
    /// CR 119.4 — the printed damage MUST sum to exactly
    /// <see cref="TotalDamage"/>. Defensive normalisation: any over/
    /// underflow from an ill-formed caller-supplied delegate is reconciled
    /// onto the first legal target.
    /// </summary>
    private static IReadOnlyDictionary<object, int> NormalizeAllocation(
        IReadOnlyDictionary<object, int>? raw,
        IReadOnlyList<object> legal)
    {
        var result = new Dictionary<object, int>();
        if (legal.Count == 0) return result;

        foreach (var t in legal) result[t] = 0;

        if (raw != null)
        {
            foreach (var (target, amount) in raw)
            {
                if (!result.ContainsKey(target)) continue;
                if (amount < 0) continue;
                result[target] = amount;
            }
        }

        var total = 0;
        foreach (var amount in result.Values) total += amount;
        var delta = TotalDamage - total;
        if (delta != 0)
        {
            var first = legal[0];
            result[first] = Math.Max(0, result[first] + delta);
        }
        return result;
    }

    /// <summary>
    /// CR 115.4 — Electrolyze's "any target" accepts creatures, players,
    /// and planeswalkers (loyalty removal handled by
    /// <see cref="Fx.DealDamageAny"/>). Battles are not modelled yet.
    /// </summary>
    private static bool IsLegalTarget(object live)
    {
        if (live is Player) return true;
        if (live is Creature) return true;
        if (live is Planeswalker) return true;
        return false;
    }
}
