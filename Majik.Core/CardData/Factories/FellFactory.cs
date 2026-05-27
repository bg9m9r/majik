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
/// Named-card factory for Fell (various sets, {1}{B}).
///
/// Sorcery. Oracle text:
///   "Destroy target creature."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{B}, mana value 2.
/// - On-resolve "Destroy target creature" effect (CR 701.7), exposed via
///   <see cref="BuildSpellDefinition"/>. Single 1..1 "target creature"
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>,
///   CandidateGatherer: all battlefield creatures across all players).
///   Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
///   honoured at the destroy site via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/>.
///   Fell's printed text has no "can't be regenerated" rider, so active
///   regeneration shields are consumed rather than bypassed.
/// - CR 608.2b illegal-target guard: if the targeted creature is no longer
///   on the battlefield at resolution, the effect is a no-op.
/// </summary>
[CardName("Fell")]
public static class FellFactory
{
    public const string CardName = "Fell";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target creature) is built on demand via
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature" request; on resolution the targeted creature is
    /// destroyed (CR 701.7) iff it is still a creature on the battlefield
    /// (CR 608.2b — illegal target → no-op).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: all creatures on the battlefield across
                    // every player. Bot ranks opponent creatures highest via
                    // Removal intent.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature",
                        () =>
                        {
                            if (raw is not Creature target) return;

                            // CR 608.2b — resolution-time legality check.
                            // Target must still be a creature on the battlefield.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // cancels the destroy; any active regeneration
                            // shield (CR 701.15) is consumed (Fell prints no
                            // "can't be regenerated" rider).
                            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
