using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Avacyn's Judgment (Eldritch Moon, {1}{R}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Madness {X}{R} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)
///    Avacyn's Judgment deals 2 damage divided as you choose among any number
///    of targets. If this spell's madness cost was paid, it deals X damage
///    divided as you choose among those permanents and/or players instead."
///
/// ## Madness is intrinsic
/// "Madness {X}{R}" needs NO factory wiring. CR 702.35 madness works for every
/// catalogued card via <see cref="Majik.Core.Keywords.MadnessCatalog"/> (this
/// card is catalogued at <c>{X}{R}</c>) consulted by the central discard funnel
/// <see cref="Fx.DiscardCard"/> — a discarded madness card is routed to exile
/// and offered for its madness cost automatically. This factory implements only
/// the divided-damage spell body.
///
/// ## Shape source
/// Card identity (name, {1}{R}, Sorcery) is loaded from
/// <c>Majik.Core/CardData/Cards/avacyns-judgment.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The resolve-time spell body is supplied
/// at cast time via <see cref="BuildSpellDefinition"/> (the runtime needs the
/// caller's target resolver, which lives on the <see cref="Game.GameContext"/>).
///
/// ## Implemented (v1)
/// - Sorcery identity at {1}{R} (the printed / normal cast cost).
/// - One 1..N "any target" request — divided damage (CR 601.2d / CR 119.4)
///   among "any number of targets" (creatures, planeswalkers, players,
///   battles). MinTargets=1 (a divided-damage spell needs at least one target;
///   CR 601.2c).
/// - <b>Madness-vs-normal total</b>: the total damage divided is the
///   discriminator the oracle text turns on — "if this spell's madness cost was
///   paid, it deals X … instead." The madness cost is <c>{X}{R}</c>, so a
///   madness cast supplies a chosen X (<see cref="Game.ChosenSpellParams.X"/>
///   is non-null); a normal {1}{R} cast supplies no X. The factory therefore
///   reads <c>chosen.X</c>: when present (madness paid) the total is X; when
///   absent (normal cast) the total is the printed 2. Mirrors
///   <see cref="ShatterskullSmashingFactory"/>'s <c>chosen.X ?? 0</c> X-spell
///   shape — no new engine mechanic needed.
/// - <b>Division</b>: caller supplies a <c>distribute</c> delegate that, given
///   the legal-at-resolution target list and the total, returns the per-target
///   allocation. Sum is normalised to exactly the total onto the first legal
///   target (CR 119.4). Default fallback: all on a single target, else ceil/
///   floor across two, else spread one-each. Same posture as
///   <see cref="ForkedBoltFactory"/> / <see cref="ShatterskullSmashingFactory"/>
///   pending an agent-driven division prompt.
/// - <b>Illegal targets at resolution</b> filtered per CR 608.2b; surviving
///   subset still takes damage. If every target became illegal, the spell does
///   nothing.
///
/// ## Deferred (v1 gaps)
/// - <b>Real divide-damage prompt</b>: CR 601.2d announces the damage division
///   at cast time. No agent-driven division prompt exists yet — the
///   <c>distribute</c> Func is the stand-in (same posture as
///   <see cref="ForkedBoltFactory"/> / <see cref="ShatterskullSmashingFactory"/>).
/// </summary>
[CardName("Avacyn's Judgment")]
public static class AvacynsJudgmentFactory
{
    public const string CardName = "Avacyn's Judgment";

    /// <summary>CR 119.4 — the normal-cast total (no madness): "deals 2 damage
    /// divided as you choose."</summary>
    public const int NormalTotalDamage = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("avacyns-judgment");

    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Sorcery)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time "deal {2 or X} damage divided among any number of
    /// targets" <see cref="SpellDefinition"/>.
    ///
    /// <para>The total is <see cref="NormalTotalDamage"/> = 2 on a normal cast
    /// and the chosen <c>X</c> on a madness cast — discriminated by
    /// <see cref="Game.ChosenSpellParams.X"/> (non-null only when the madness
    /// {X}{R} cost was paid). CR 702.35 / oracle: "if this spell's madness cost
    /// was paid, it deals X damage … instead."</para>
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="Game.GameContext"/> (chosen target → live game object).</param>
    /// <param name="distribute">Optional per-target allocation strategy —
    /// given (legalTargets, total) return target → damage. Sum must equal the
    /// total; any over/underflow is reconciled onto the first legal target.
    /// When null, the default split is used.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        Func<IReadOnlyList<object>, int, IReadOnlyDictionary<object, int>>? distribute = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            // HasVariableX so the madness {X}{R} cast prompts for X. A normal
            // {1}{R} cast leaves X null and the total defaults to 2.
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "any number of targets",
                    MinTargets: 1,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                // CR / oracle: madness paid → total = X; otherwise → total = 2.
                // chosen.X is non-null only when the {X}{R} madness cost was paid.
                var total = chosen.X ?? NormalTotalDamage;

                var rawTargets = chosen.Targets.Count > 0
                    ? chosen.Targets[0]
                    : Array.Empty<object>();

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: deal {total} damage divided among any number of targets",
                        () =>
                        {
                            if (total <= 0) return;

                            // CR 608.2b — drop illegal-at-resolution targets.
                            var legal = new List<object>();
                            foreach (var token in rawTargets)
                            {
                                var live = resolver(token);
                                if (IsLegalTarget(live)) legal.Add(live);
                            }
                            if (legal.Count == 0) return;

                            var allocation = distribute != null
                                ? NormalizeAllocation(distribute(legal, total), legal, total)
                                : DefaultAllocation(legal, total);

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
    /// Default damage division across the legal target list:
    ///   <list type="bullet">
    ///     <item>1 target → all <paramref name="total"/> on it.</item>
    ///     <item>2 targets → ceil(total/2) on the first, the rest on the second.</item>
    ///     <item>N&gt;2 targets → spread as evenly as possible, remainder on the
    ///       leading targets.</item>
    ///   </list>
    /// </summary>
    public static IReadOnlyDictionary<object, int> DefaultAllocation(
        IReadOnlyList<object> legal,
        int total)
    {
        ArgumentNullException.ThrowIfNull(legal);
        if (legal.Count == 0) return new Dictionary<object, int>();
        if (legal.Count == 1) return new Dictionary<object, int> { [legal[0]] = total };

        // Spread evenly; the first (total % count) targets get the +1 remainder.
        var result = new Dictionary<object, int>();
        var baseEach = total / legal.Count;
        var remainder = total % legal.Count;
        for (var i = 0; i < legal.Count; i++)
        {
            result[legal[i]] = baseEach + (i < remainder ? 1 : 0);
        }
        return result;
    }

    /// <summary>
    /// CR 119.4 — the damage MUST sum to exactly <paramref name="total"/>. Any
    /// over/underflow from the caller-supplied delegate is reconciled onto the
    /// first legal target.
    /// </summary>
    private static IReadOnlyDictionary<object, int> NormalizeAllocation(
        IReadOnlyDictionary<object, int>? raw,
        IReadOnlyList<object> legal,
        int total)
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

        var sum = result.Values.Sum();
        var delta = total - sum;
        if (delta != 0)
        {
            var first = legal[0];
            result[first] = Math.Max(0, result[first] + delta);
        }
        return result;
    }

    /// <summary>
    /// CR 115.4 / oracle ("any number of targets" / "those permanents and/or
    /// players"): legal targets are players and battlefield permanents
    /// (creatures, planeswalkers, etc.). <see cref="Fx.DealDamageAny"/> maps
    /// planeswalker damage to loyalty removal (CR 306.7).
    /// </summary>
    private static bool IsLegalTarget(object live)
    {
        if (live is Player) return true;
        if (live is Permanent p) return p.Zone == ZoneType.Battlefield;
        return false;
    }
}
