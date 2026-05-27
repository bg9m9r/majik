using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Smash to Smithereens (Shadowmoor / many reprints,
/// {1}{R}).
///
/// Instant. Oracle text:
///   "Destroy target artifact. Smash to Smithereens deals 3 damage to
///   that artifact's controller."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{R}, Red.
/// - <b>Destroy target artifact</b> — <see cref="BuildDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target artifact"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding permanents that have type Artifact
///   (CR 301).
/// - On resolution: re-checks the target is still a Permanent on the
///   Battlefield AND has type Artifact (CR 608.2b illegal-target gate).
///   If the check fails the entire spell does nothing (no destroy, no damage).
/// - Controller is captured BEFORE the destroy call so the damage recipient
///   is stable even after the permanent leaves the battlefield.
/// - After destroying, deals 3 damage to the artifact's controller via
///   <see cref="Fx.DealDamage"/> (CR 119).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by the Destroy-reason gate in MoveToGraveyard — same posture
/// as <see cref="DisenchantFactory"/> / <see cref="DoomBladeFactory"/>.
/// </summary>
[CardName("Smash to Smithereens")]
public static class SmashToSmithereensFactory
{
    public const string CardName = "Smash to Smithereens";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target artifact + 3 damage to its controller) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target artifact; deal 3 to its controller"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve:
    /// 1. Validates the resolved target is still a <see cref="Permanent"/> on
    ///    the Battlefield AND has type <see cref="CardType.Artifact"/>
    ///    (CR 608.2b — illegal target at resolution → entire spell is no-op;
    ///    no destroy, no damage).
    /// 2. Captures the controller BEFORE destroying (card leaves battlefield on
    ///    destroy, making Controller unreliable afterwards).
    /// 3. Destroys via <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    ///    <see cref="ZoneMoveReason.Destroy"/> (CR 701.7).
    /// 4. Deals <see cref="Damage"/> (3) to the captured controller via
    ///    <see cref="Fx.DealDamage"/> (CR 119).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield permanents
                    // that are artifacts (CR 301).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: destroy target artifact and deal {Damage} to its controller",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // Target must still be on the battlefield as an artifact.
                            // If not, the entire spell does nothing (no destroy, no damage).
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Oracle constraint: target must be an artifact at resolution.
                            if (!target.HasType(CardType.Artifact)) return;

                            // Capture controller BEFORE destroying: the permanent
                            // leaves the battlefield during destruction, after which
                            // Controller may be null or stale.
                            var controller = target.Controller;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12) and
                            // regeneration (CR 701.15) handled via the Destroy-reason
                            // gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);

                            // CR 119 — deal 3 damage to the artifact's controller.
                            // Both clauses key on the same legal-target check (CR 608.2b),
                            // so damage only fires when destroy was attempted.
                            if (controller is not null)
                                Fx.DealDamage(controller, Damage);
                        }),
                };
            });
    }
}
