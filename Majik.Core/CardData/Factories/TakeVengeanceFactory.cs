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
/// Named-card factory for Take Vengeance (Amonkhet, {1}{W}).
///
/// Sorcery. Oracle text:
///   "Destroy target tapped creature."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{W}, owner / controller.
/// - <b>Destroy target tapped creature</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> with a single 1..1
///   "target tapped creature" <see cref="TargetRequest"/>. The
///   <see cref="TargetRequest.CandidateGatherer"/> restricts legal
///   targets to <see cref="Creature"/>s whose <see cref="Permanent.IsTapped"/>
///   flag is <see langword="true"/> at targeting time (CR 115.5 — a target
///   must be legal when chosen).
///
/// On resolution the targeted creature is destroyed via
/// <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7) iff it is
/// still on the battlefield AND still tapped (CR 608.2b — if it untapped
/// after being targeted, it is no longer a legal target and the spell
/// does nothing).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by the <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/>
/// gate in <see cref="OracleSpellBinder.MoveToGraveyard"/>.
/// </summary>
[CardName("Take Vengeance")]
public static class TakeVengeanceFactory
{
    public const string CardName        = "Take Vengeance";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target tapped creature) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target tapped creature" <see cref="SpellDefinition"/>.
    ///
    /// The <see cref="TargetRequest.CandidateGatherer"/> enumerates every
    /// <see cref="Creature"/> on any player's battlefield that is currently
    /// tapped. On resolve, the legality guard re-checks that the target is
    /// still a creature on the battlefield AND still tapped (CR 608.2b).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes:         Array.Empty<string>(),
            HasVariableX:  false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description:      "target tapped creature",
                    MinTargets:       1,
                    MaxTargets:       1,
                    LegalCandidates:  Array.Empty<object>(),
                    Intent:           BotIntent.Removal,
                    // CR 115.5 — a target must be legal when chosen.
                    // Gather every tapped creature on any battlefield.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.IsTapped)
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
                        $"{CardName}: destroy target tapped creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // Target must still be a creature on the battlefield.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield)  return;

                            // CR 608.2b — tapped restriction: if the creature
                            // untapped after being targeted the spell does nothing.
                            if (!target.IsTapped) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
