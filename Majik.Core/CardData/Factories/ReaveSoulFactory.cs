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
/// Named-card factory for Reave Soul (Magic Origins, {1}{B}).
///
/// Sorcery. Oracle text:
///   "Destroy target creature with power 3 or less."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{B}, owner / controller.
/// - <b>Destroy target creature with power 3 or less</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature with power 3 or less"
///   <see cref="TargetRequest"/>. On resolution the chosen creature is
///   filtered for power ≤ 3 (CR 608.2b — illegal-target re-check at
///   resolution) and destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate.
/// </summary>
[CardName("Reave Soul")]
public static class ReaveSoulFactory
{
    public const string CardName = "Reave Soul";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (destroy creature power ≤ 3) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target creature with power 3 or less"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates that the target is still a Creature on the
    /// Battlefield AND has power ≤ 3 (CR 608.2b — illegal-target filter at
    /// resolution). When valid, destroys the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
    /// indestructible (CR 702.12) and regeneration shields (CR 701.15) are
    /// honoured at the destroy site.
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
                    Description: "target creature with power 3 or less",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: gather every creature with power ≤ 3
                    // on any battlefield. Removal intent in the bot ranker
                    // prioritises the opponent's most threatening small creature.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.Power <= 3)
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw      = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature with power 3 or less",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // Power filter: must still be ≤ 3 at resolution.
                            if (target.Power > 3) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration shields (CR 701.15) are
                            // honoured via the Destroy-reason gate in
                            // MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
