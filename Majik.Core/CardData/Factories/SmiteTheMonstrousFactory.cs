using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Smite the Monstrous (various printings, {3}{W}).
///
/// Instant. Oracle text:
///   "Destroy target creature with power 4 or greater."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {3}{W}, owner / controller.
/// - <b>Destroy target creature with power 4 or greater</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature with power 4 or greater"
///   <see cref="TargetRequest"/>. On resolution the chosen creature is
///   filtered for power ≥ 4 (CR 608.2b — illegal-target re-check) and
///   destroyed via <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7).
///
/// Note: Unlike Reprisal, Smite the Monstrous has no "can't be regenerated"
/// rider — it uses a normal destroy (<see cref="ZoneMoveReason.Destroy"/>),
/// so active regeneration shields (CR 701.15) are consumed as normal.
/// Indestructible (CR 702.12) still cancels the destroy (CR 702.12b).
/// </summary>
[CardName("Smite the Monstrous")]
public static class SmiteTheMonstrousFactory
{
    public const string CardName = "Smite the Monstrous";
    public const string PrintedManaCost = "{3}{W}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (destroy creature power ≥ 4) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target creature with power 4 or greater"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates that the target is still a Creature on the
    /// Battlefield AND has power ≥ 4 (CR 608.2b — illegal-target filter at
    /// resolution). When valid, destroys the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
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
                    Description: "target creature with power 4 or greater",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: gather every creature with power ≥ 4
                    // on any battlefield. Removal intent in the bot ranker
                    // prioritises the opponent's largest threat.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.Power >= 4)
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature with power 4 or greater",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // Power filter: must still be ≥ 4 at resolution.
                            if (target.Power < 4) return;

                            // CR 701.7 — Destroy. Normal destroy (regeneration
                            // shields are consumed, not bypassed — Smite the
                            // Monstrous has no "can't be regenerated" rider).
                            // Indestructible (CR 702.12) still cancels.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
