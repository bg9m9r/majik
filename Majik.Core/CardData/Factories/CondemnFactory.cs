using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Condemn (Tenth Edition / Ravnica, {W}).
///
/// Instant. Oracle text:
///   "Put target attacking creature on the bottom of its owner's library.
///    Its controller gains life equal to its toughness."
///
/// ## Implemented (v1)
/// - Instant {W} (white) card shape with owner / controller wired.
/// - <b>Target ATTACKING creature</b> — single 1..1
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>).
///   The candidate gatherer is supplied by the caller via
///   <paramref name="attackerLookup"/>, exactly as
///   <see cref="RazorgrassAmbushFactory"/> / <see cref="SettleTheWreckageFactory"/>
///   inject combat state:
///     <list type="bullet">
///       <item>Production callers wire a delegate reading
///         <see cref="CombatManager.CurrentCombat"/>'s
///         <see cref="Combat.Attackers"/> (CR 506.2 — attacking creatures
///         are those declared as attackers this combat that haven't been
///         removed from combat).</item>
///       <item>Test callers inject the list directly — no global registry
///         to mock.</item>
///       <item>A null lookup (or one returning null / empty) means no legal
///         candidates — the shape-only / dispatcher path with no live combat.</item>
///     </list>
/// - <b>Resolve — bottom of owner's library + lifegain by toughness</b>
///   (<see cref="Resolve"/>):
///     <list type="number">
///       <item>CR 608.2b — illegal-target re-check: the target must still be
///         a <see cref="Creature"/> on the battlefield, else no-op.</item>
///       <item>CR 112.7a — snapshot toughness BEFORE the zone move, since the
///         lifegain references the creature as last seen on the battlefield.
///         Negative toughness floors to zero (CR 119.3 — a player can't gain
///         a negative amount of life; <see cref="Player.GainLife"/> also
///         rejects negatives).</item>
///       <item>Put the creature on the bottom of its OWNER's library
///         (CR 701.x library-bottom move). <see cref="Zone.AddCard"/> appends
///         to the bottom of the list — the engine's library-bottom primitive.</item>
///       <item>The exiled-from-battlefield creature's CONTROLLER (snapshotted
///         before the move, CR 608.2b last-known-information) gains life equal
///         to the snapshotted toughness (CR 119.3).</item>
///     </list>
///
/// ## References
/// - Combat-creature injection + attacking-only targeting:
///   <see cref="RazorgrassAmbushFactory"/> / <see cref="SettleTheWreckageFactory"/>.
/// - Single-target white relocation + lifegain (power-vs-toughness aside):
///   <see cref="SwordsToPlowsharesFactory"/>.
/// </summary>
[CardName("Condemn")]
public static class CondemnFactory
{
    public const string CardName = "Condemn";
    public const string Cost = "{W}";

    /// <summary>CardDef DSL — card shape only. Bottom-of-library + lifegain
    /// body lives in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, Cost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "put target attacking creature on the bottom of its owner's
    /// library; its controller gains life equal to its toughness"
    /// <see cref="SpellDefinition"/>.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
    /// <param name="attackerLookup">Returns the attacking creatures currently
    /// in combat. Production callers wire this from
    /// <see cref="CombatManager.CurrentCombat"/>'s <see cref="Combat.Attackers"/>;
    /// test callers inject a list directly. Null (or a delegate returning
    /// null / empty) is legal — the shape-only path with no live combat
    /// reports no candidates.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Func<IReadOnlyList<Creature>>? attackerLookup = null)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target attacking creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: only attacking creatures from the current
                    // combat (CR 506.2). Injected by the caller so the factory
                    // stays testable without a live game loop.
                    CandidateGatherer: _ =>
                    {
                        if (attackerLookup == null)
                            return Array.Empty<object>();

                        var pool = attackerLookup() ?? Array.Empty<Creature>();
                        return pool.Cast<object>().ToList();
                    }),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: bottom target attacking creature on its owner's library; its controller gains life equal to its toughness",
                        () => Resolve(resolved)),
                };
            });
    }

    // -------------------------------------------------------------------------
    // Resolution body
    // -------------------------------------------------------------------------

    private static void Resolve(object resolved)
    {
        // CR 608.2b — illegal-target re-check: only act when the resolved
        // target is still a Creature on the battlefield.
        if (resolved is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // CR 112.7a — sample toughness BEFORE the zone move; the lifegain
        // references the creature as last seen on the battlefield. Floor
        // negative toughness to zero (CR 119.3).
        var snapshotToughness = target.Toughness;
        var lifeAmount = snapshotToughness < 0 ? 0 : snapshotToughness;

        // Snapshot controller BEFORE moving — lifegain goes to "its
        // controller" at resolution (CR 608.2b last-known-information).
        var targetController = target.Controller ?? target.Owner;

        // Put on the BOTTOM of its OWNER's library (CR 701.x). Zone.AddCard
        // appends to the bottom of the underlying list — the engine's
        // library-bottom primitive (see TurntimberSymbiosisFactory).
        var owner = target.Owner;
        if (owner != null)
        {
            owner.Zones.Battlefield.RemoveCard(target);
            owner.Zones.Library.AddCard(target);
        }
        target.SetZone(ZoneType.Library);

        // Lifegain to the relocated creature's controller (CR 119.3).
        if (targetController != null && lifeAmount > 0)
        {
            targetController.GainLife(lifeAmount);
        }
    }
}
