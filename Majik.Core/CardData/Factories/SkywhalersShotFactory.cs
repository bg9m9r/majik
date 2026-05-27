using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skywhaler's Shot (Kaladesh / various reprints, {2}{W}).
///
/// Instant. Oracle text:
///   "Destroy target creature with power 3 or greater. Scry 1."
///
/// ## Implemented
/// - Instant shape, mana cost {2}{W}, white.
/// - <b>Destroy target creature with power 3 or greater</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature with power 3 or greater"
///   <see cref="TargetRequest"/>. On resolution the chosen creature is
///   filtered for power ≥ 3 (CR 608.2b — illegal-target re-check) and
///   destroyed via <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7).
///   Indestructible (CR 702.12) still cancels the destroy (CR 702.12b).
/// - <b>Scry 1</b> for the casting player immediately after the destroy
///   (CR 701.20 / CR 608.2e left-to-right clause ordering). The controller's
///   registered <see cref="IPlayerAgent"/> is consulted when present; the
///   pre-agent default sends the peeked card to the bottom of the library
///   (matching <see cref="MagmaJetFactory"/> / <see cref="DissolveFactory"/>).
///   Empty library: scry short-circuits without throwing.
/// </summary>
[CardName("Skywhaler's Shot")]
public static class SkywhalersShotFactory
{
    public const string CardName = "Skywhaler's Shot";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>CardDef DSL — card shape only. Destroy + scry body is
    /// supplied at cast time via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target creature with power 3 or greater, then scry 1"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve:
    ///   1. Validates that the target is still a Creature on the Battlefield
    ///      AND has power ≥ 3 (CR 608.2b). When valid, destroys the target via
    ///      <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7).
    ///   2. Caster scrys 1 (CR 701.20). The scry happens regardless of whether
    ///      the destroy was a no-op (target became illegal), mirroring the
    ///      card's unconditional second clause.
    /// </summary>
    /// <param name="caster">The player who cast Skywhaler's Shot; receives
    /// the Scry 1 after the destroy effect.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature with power 3 or greater",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: gather every creature with power ≥ 3
                    // on any battlefield. Removal intent in the bot ranker
                    // prioritises the opponent's largest threat.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.Power >= 3)
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw      = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: destroy target creature with power 3 or greater, then scry 1",
                        () =>
                        {
                            // CR 608.2e step 1 — destroy target creature (power ≥ 3).
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is Creature target
                                && target.Zone == ZoneType.Battlefield
                                && target.Power >= 3)
                            {
                                // CR 701.7 — Destroy. Normal destroy (no "can't be
                                // regenerated" rider on Skywhaler's Shot). Active
                                // regeneration shields (CR 701.15) are consumed as
                                // normal; indestructible (CR 702.12) still cancels.
                                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
                            }

                            // CR 608.2e step 2 / CR 701.20 — Scry 1 for the caster.
                            // Unconditional: fires even if the destroy was a no-op.
                            var peeked = ScryAction.Peek(caster, 1);
                            if (peeked.Count == 0)
                            {
                                return; // empty library — scry short-circuits cleanly.
                            }

                            var agent = AgentRegistry.Get(caster);
                            ScryAction.ScryDecision decision;
                            if (agent != null)
                            {
                                // TODO: drop sync-over-async once IEffect.Execute becomes async.
                                decision = agent.ChooseScryDecisionAsync(null, peeked)
                                    .GetAwaiter().GetResult();
                            }
                            else
                            {
                                // Pre-agent default: send the peeked card to the bottom.
                                decision = new ScryAction.ScryDecision(
                                    ToBottom: peeked.ToList(),
                                    TopOrder: Array.Empty<ICard>());
                            }

                            ScryAction.Apply(caster, peeked.Count, decision);
                        }),
                };
            });
    }
}
