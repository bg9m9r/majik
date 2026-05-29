using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FIRE half of the split card Fire // Ice
/// (Apocalypse / various reprints, {1}{R} // {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Fire deals 2 damage divided as you choose among one or two targets."
///
/// Sister half — <see cref="IceFactory"/> ({1}{U}; "Tap target permanent.
/// Draw a card.").
///
/// ## Split-card modelling (CR 712 / CR 709)
///
/// A split card is a single physical card with two halves; the caster picks
/// one half on cast and casts only that half. v1 models each printed half as
/// its own <c>[CardName]</c>-dispatched factory — the same minimal posture
/// the engine uses for the modal double-faced card Sink into Stupor //
/// Soporific Springs (<see cref="SinkIntoStuporFactory"/>):
/// <list type="bullet">
///   <item>Casting Fire → <see cref="NamedCardFactory"/> resolves
///     <c>"Fire"</c> → this factory → an <see cref="Instant"/> with the
///     divided-damage effect.</item>
///   <item>Casting Ice → <see cref="NamedCardFactory"/> resolves
///     <c>"Ice"</c> → <see cref="IceFactory"/> → an <see cref="Instant"/>
///     with the tap + draw effect.</item>
/// </list>
/// The combined seed row <c>"Fire // Ice"</c> flips <c>IsImplemented</c> via
/// the front-face check in <see cref="EmbeddedCardRepository"/> because the
/// front half <c>"Fire"</c> is in the <see cref="ImplementedCardNames"/>
/// registry. Each half also carries an <see cref="MdfcState"/> face tracker
/// (front = "Fire", back = "Ice") so callers can observe the other half's
/// printed name from either object — same informational role MdfcState plays
/// for the Sink // Soporific MDFC.
///
/// ## Implemented (v1)
/// - Instant identity at {1}{R} (red, mana value 2), built from the embedded
///   JSON def (<c>fire.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="MdfcState"/> attached on the front half (Fire).
/// - <b>Divided damage</b> — single 1..2 "any target" request whose
///   <see cref="TotalDamage"/> = 2 is divided across the chosen targets via
///   the same caller-supplied <c>distribute</c> delegate posture as
///   <see cref="ForkedBoltFactory"/> (CR 601.2d announces the division at
///   cast time; the engine has no agent-driven division prompt yet — the
///   delegate is the stand-in). Default split: 1 target → all 2; 2 targets →
///   1 + 1. Per-target damage is dealt via <see cref="Fx.DealDamageAny"/>
///   (CR 120.3 / CR 306.7 — planeswalker damage becomes loyalty removal).
/// - <b>Illegal targets at resolution</b> — filtered per CR 608.2b; the
///   spell resolves with whatever legal subset remains, and fizzles only if
///   every target became illegal.
///
/// Unlike <see cref="ForkedBoltFactory"/> (printed "creatures and/or
/// players"), Fire's printed text is the broader modern "one or two targets"
/// = any target, so creatures, players, planeswalkers, and battles are all
/// legal recipients.
///
/// ## Deferred (v1 gaps — shared with the burn cycle)
/// - Real agent-driven divide-damage prompt (CR 601.2d). The
///   <c>distribute</c> Func is the stand-in until that ships.
/// </summary>
[CardName("Fire")]
public static class FireFactory
{
    public const string CardName = "Fire";
    public const string SisterName = "Ice";
    public const string Slug = "fire";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>CR 119.4 — total damage divided across the chosen targets.</summary>
    public const int TotalDamage = 2;

    /// <summary>
    /// Build the Fire half as an Instant from the embedded JSON def, with the
    /// <see cref="MdfcState"/> face tracker attached (front = Fire).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 712 — attach the split-card face tracker so the sister half's
        // printed name (Ice) is observable from the Fire object. Starts on
        // the front half. Informational only, matching the Sink // Soporific
        // MDFC posture.
        card.MdfcState = new MdfcState(CardName, SisterName);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Fire is cast. One
    /// 1..2 "any target" request, no X. On resolution the
    /// <paramref name="distribute"/> delegate (or the default fallback)
    /// splits <see cref="TotalDamage"/> = 2 across the legal-at-resolution
    /// targets, routed through <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="distribute">Optional per-target allocation strategy —
    /// given the legal-at-resolution targets, return target → damage. Sum
    /// must equal 2; over/underflow is reconciled onto the first legal
    /// target. When null, the default splits "all 2 on one target" /
    /// "1 each across two".</param>
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
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 2,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            },
            EffectFactory: chosen =>
            {
                var rawTargets = chosen.Targets[0];
                return new IEffect[]
                {
                    Fx.Inline("Fire: divide 2 damage among one or two targets", () =>
                    {
                        // CR 608.2b — drop illegal-at-resolution picks.
                        var legal = new List<object>();
                        foreach (var token in rawTargets)
                        {
                            var live = resolver(token);
                            if (IsLegalDamageTarget(live))
                            {
                                legal.Add(live);
                            }
                        }
                        if (legal.Count == 0) return; // all illegal — fizzle

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
    /// Default damage division — 1 legal target → all 2 on it; 2 legal
    /// targets → 1 each (CR 119.4). Mirrors <see cref="ForkedBoltFactory"/>.
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
    /// CR 119.4 — dealt damage must sum to exactly <see cref="TotalDamage"/>.
    /// Defensive normalisation: any over/underflow from an ill-formed
    /// caller-supplied delegate is reconciled onto the first legal target.
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
    /// "Any target" (CR 115.4) — creatures, players, planeswalkers, and
    /// battles are legal recipients. <see cref="Fx.DealDamageAny"/> handles
    /// the per-type routing; this gate only verifies the object is one of
    /// the damageable kinds so a stale token resolves to a fizzle.
    /// </summary>
    private static bool IsLegalDamageTarget(object live) =>
        live is Player or Creature or Planeswalker;
}
