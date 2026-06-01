using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Shatterskull Smashing // Shatterskull, the Hammer Pass
/// (Zendikar Rising, {X}{R}{R}).
///
/// Sorcery. Oracle text (front):
///   "Shatterskull Smashing deals X damage divided as you choose among up
///    to two target creatures and/or planeswalkers. If X is 6 or more,
///    Shatterskull Smashing deals twice X damage divided as you choose
///    among them instead."
///
/// Back face — <see cref="ShatterskullTheHammerPassFactory"/> (Land —
/// "As this land enters, you may pay 3 life. If you don't, it enters
/// tapped." / "{T}: Add {R}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face modelled by two independent <c>[CardName]</c>-dispatched
/// factories — same architecture as <see cref="SinkIntoStuporFactory"/> /
/// <see cref="SoporificSpringsFactory"/> and
/// <see cref="SunderingEruptionFactory"/> / <see cref="VolcanicFissureFactory"/>.
///
/// ## Implemented (v1)
///
/// - Sorcery identity at <c>{X}{R}{R}</c>, mono-red, owner/controller wired.
/// - <see cref="MdfcState"/> attached (front = "Shatterskull Smashing",
///   back = "Shatterskull, the Hammer Pass"); starts on the front face.
/// - <see cref="SpellDefinition.HasVariableX"/> = true — cast flow prompts
///   for X. Cost is {X}{R}{R}; player commits X+2 mana.
/// - One 0..2 "target creature and/or planeswalker" request. MinTargets=0
///   ("up to two" per oracle; zero is legal, though X=0 damages nothing).
/// - Resolution:
///     <list type="bullet">
///       <item>If X &lt; 6: total damage = X, divided among legal targets.</item>
///       <item>If X ≥ 6: total damage = 2X, divided among legal targets
///         (CR 119.4 — "deals twice X damage divided … instead").</item>
///       <item>Caller-supplied <c>distribute</c> strategy provides the
///         per-target allocation (sum must equal the total; normalisation
///         mirrors <see cref="ForkedBoltFactory"/>). Default split:
///         remaining/2 on the first target (ceil), rest on the second.</item>
///       <item>Planeswalker targets lose loyalty (CR 306.7) via
///         <see cref="Fx.DealDamageAny"/>; creature targets take marked
///         damage (CR 119.2).</item>
///       <item>CR 608.2b — illegal-at-resolution targets (off battlefield,
///         wrong type) are silently dropped. If none remain, the spell
///         does nothing.</item>
///     </list>
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real divide-damage prompt</b>: CR 601.2d announces the damage
///   division at cast time. No agent-driven division prompt exists yet —
///   the <c>distribute</c> Func is the stand-in (same posture as
///   <see cref="ForkedBoltFactory"/> / <see cref="FuryFactory"/>).
///
/// ## References
///
/// - <see cref="ForkedBoltFactory"/> — X-independent divided-damage primitive
///   with identical 1..2 split + normalisation shape.
/// - <see cref="BonfireOfTheDamnedFactory"/> — X-spell pattern with
///   HasVariableX=true.
/// - <see cref="SunderingEruptionFactory"/> / <see cref="VolcanicFissureFactory"/>
///   — companion MDFC pair showing the same two-factory architecture.
/// </summary>
[CardName("Shatterskull Smashing")]
public static class ShatterskullSmashingFactory
{
    public const string CardName = "Shatterskull Smashing";
    public const string BackName = "Shatterskull, the Hammer Pass";
    public const string PrintedManaCost = "{X}{R}{R}";

    /// <summary>
    /// Construct Shatterskull Smashing as a Sorcery with owner / controller
    /// wired and the <see cref="MdfcState"/> face tracker attached. The
    /// resolve-time <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name is observable from the front-face card object.
        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (deferral #3, real cast-either-face). The
        // back face is the LAND back face played with no stack; MdfcCastFlow
        // offers the controller a face choice at cast time and materializes
        // a fresh back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                ShatterskullTheHammerPassFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time "X damage divided among up to two target
    /// creatures and/or planeswalkers; 2X if X ≥ 6"
    /// <see cref="SpellDefinition"/>.
    ///
    /// <see cref="SpellDefinition.HasVariableX"/> is true; the cast flow
    /// prompts for X and stores it in <see cref="ChosenSpellParams.X"/>.
    /// </summary>
    /// <param name="resolver">Target resolver — maps the chosen target token
    /// to the live game object (creature or planeswalker).</param>
    /// <param name="distribute">Optional per-target allocation strategy.
    /// Receives (legalTargets, totalDamage) and returns the per-target
    /// allocation dictionary. Sum must equal totalDamage; any over/underflow
    /// is normalised onto the first legal target (CR 119.4). When null, the
    /// default split (ceil(total/2) on the first, floor(total/2) on the
    /// second — or all total on a single target) is used.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        Func<IReadOnlyList<object>, int, IReadOnlyDictionary<object, int>>? distribute = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to two target creatures and/or planeswalkers",
                    MinTargets: 0,
                    MaxTargets: 2,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                // CR 119.4 / oracle: "deals twice X damage … if X is 6 or more".
                var total = x >= 6 ? x * 2 : x;

                var rawTargets = chosen.Targets.Count > 0 ? chosen.Targets[0] : Array.Empty<object>();

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: deal {total} damage divided among up to two targets (X={x})",
                        () =>
                        {
                            if (total <= 0) return;

                            // Resolve targets, dropping any that are illegal
                            // at resolution (CR 608.2b).
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
    /// Default damage division — splits <paramref name="total"/> across the
    /// legal target list:
    ///   <list type="bullet">
    ///     <item>1 legal target → all <paramref name="total"/> on it.</item>
    ///     <item>2 legal targets → ceil(total/2) on the first, the rest on
    ///       the second.</item>
    ///   </list>
    /// </summary>
    public static IReadOnlyDictionary<object, int> DefaultAllocation(
        IReadOnlyList<object> legal,
        int total)
    {
        ArgumentNullException.ThrowIfNull(legal);
        if (legal.Count == 0) return new Dictionary<object, int>();
        if (legal.Count == 1) return new Dictionary<object, int> { [legal[0]] = total };

        // 2 legal targets: ceil on first, floor on second.
        var first = (total + 1) / 2;
        var second = total - first;
        return new Dictionary<object, int>
        {
            [legal[0]] = first,
            [legal[1]] = second,
        };
    }

    /// <summary>
    /// CR 119.4 — the damage MUST sum to exactly <paramref name="total"/>.
    /// Any over/underflow from the caller-supplied delegate is reconciled
    /// onto the first legal target.
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
    /// CR 115.4 / oracle: legal targets are creatures and planeswalkers
    /// that are on the battlefield.
    /// </summary>
    private static bool IsLegalTarget(object live)
    {
        if (live is Creature c && c.Zone == ZoneType.Battlefield) return true;
        if (live is Planeswalker pw && pw.Zone == ZoneType.Battlefield) return true;
        return false;
    }
}
