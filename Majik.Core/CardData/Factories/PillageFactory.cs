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
/// Named-card factory for Pillage (Alliances / various reprints, {1}{R}{R}).
///
/// Sorcery. Oracle text:
///   "Destroy target artifact or land. It can't be regenerated."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{R}{R}, Red, owner / controller.
/// - <b>Destroy target artifact or land</b> — <see cref="BuildDefinition"/>
///   returns a <see cref="SpellDefinition"/> with a single 1..1
///   "target artifact or land" <see cref="TargetRequest"/>. The live
///   <c>CandidateGatherer</c> walks every player's battlefield, yielding
///   permanents that have type Artifact or Land (CR 301 / CR 305). Mirrors
///   <see cref="NaturalizeFactory"/> ("destroy target artifact or
///   enchantment"), swapping the enchantment slot for land.
/// - On resolution: re-checks the target is still a Permanent on the
///   Battlefield (CR 608.2b illegal-target gate), and has type Artifact or
///   Land; then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>.
///
/// The "It can't be regenerated" rider is honoured via
/// <see cref="ZoneMoveReason.DestroyNoRegeneration"/> (same posture as
/// <see cref="TerminateFactory"/>): indestructible (CR 702.12) still cancels
/// the destroy (CR 702.12b is unconditional), but any active regeneration
/// shield (CR 701.15) is bypassed rather than consumed.
/// </summary>
[CardName("Pillage")]
public static class PillageFactory
{
    public const string CardName = "Pillage";
    public const string PrintedManaCost = "{1}{R}{R}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target artifact or land, can't be regenerated) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target artifact or land" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates that the resolved target is still a
    /// <see cref="Permanent"/> on the Battlefield AND has type
    /// <see cref="CardType.Artifact"/> or <see cref="CardType.Land"/>
    /// (CR 608.2b — illegal target at resolution → no-op); then destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.DestroyNoRegeneration"/> (CR 701.7) so the
    /// "can't be regenerated" rider (CR 701.15) is honoured at the destroy
    /// site while indestructible (CR 702.12) still cancels the destroy.
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
                    Description: "target artifact or land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield permanents
                    // that are artifacts or lands (CR 301 / CR 305).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Land))
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
                        $"{CardName}: destroy target artifact or land",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Oracle constraint: target must be artifact or
                            // land at resolution (CR 608.2b).
                            if (!target.HasType(CardType.Artifact)
                                && !target.HasType(CardType.Land)) return;

                            // CR 701.7 — Destroy. "It can't be regenerated"
                            // is honoured via DestroyNoRegeneration:
                            // indestructible (CR 702.12) still cancels the
                            // destroy, but any active regeneration shield
                            // (CR 701.15) is bypassed.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.DestroyNoRegeneration);
                        }),
                };
            });
    }
}
