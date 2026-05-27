using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shatter (Alpha, {1}{R}).
///
/// Instant. Oracle text:
///   "Destroy target artifact."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{R}, Red.
/// - <b>Destroy target artifact</b> — <see cref="BuildDefinition"/>
///   returns a <see cref="SpellDefinition"/> with a single 1..1
///   "target artifact" <see cref="TargetRequest"/>. The live
///   <c>CandidateGatherer</c> walks every player's battlefield, yielding
///   permanents that have type Artifact (CR 301).
/// - On resolution: re-checks the target is still a Permanent on the
///   Battlefield (CR 608.2b illegal-target gate), and has type Artifact;
///   then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by the Destroy-reason gate in MoveToGraveyard — same posture
/// as <see cref="DisenchantFactory"/> / <see cref="NaturalizeFactory"/>.
/// </summary>
[CardName("Shatter")]
public static class ShatterFactory
{
    public const string CardName = "Shatter";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target artifact) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target artifact" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates that the resolved target is still a
    /// <see cref="Permanent"/> on the Battlefield AND has type
    /// <see cref="CardType.Artifact"/> (CR 608.2b — illegal target at
    /// resolution → no-op); then destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible /
    /// regeneration shields are honoured at the destroy site.
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
                    new Effect(
                        $"{CardName}: destroy target artifact",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Oracle constraint: target must be an artifact at
                            // resolution (CR 608.2b). Enchantments, creatures,
                            // and other permanents are not valid targets.
                            if (!target.HasType(CardType.Artifact)) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
