using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Befoul (Invasion / various reprints, {2}{B}{B}).
///
/// Sorcery. Oracle text:
///   "Destroy target land or nonblack creature. It can't be regenerated."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}{B}, owner / controller.
/// - <b>Destroy target land or nonblack creature</b> — <see cref="BuildDefinition"/>
///   returns a <see cref="SpellDefinition"/> with a single 1..1
///   "target land or nonblack creature" <see cref="TargetRequest"/>. On
///   resolution the target is validated (CR 608.2b) and dispatched:
///   <list type="bullet">
///     <item>If the target is a <see cref="Land"/> on the Battlefield →
///       destroy (no-regen, CR 701.7 + rider).</item>
///     <item>If the target is a <see cref="Creature"/> on the Battlefield
///       AND is nonblack (CR 105 — colour from mana cost pips) →
///       destroy (no-regen, CR 701.7 + rider).</item>
///     <item>Otherwise → no-op (CR 608.2b illegal-target at resolution).</item>
///   </list>
///
/// The "it can't be regenerated" rider is honoured via
/// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>,
/// so any active regeneration shield (CR 701.15) is bypassed; indestructible
/// (CR 702.12) still cancels the destroy.
/// </summary>
[CardName("Befoul")]
public static class BefoulFactory
{
    public const string CardName = "Befoul";
    public const string PrintedManaCost = "{2}{B}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target land or nonblack creature) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target land or nonblack creature" <see cref="SpellDefinition"/>.
    ///
    /// On resolve:
    /// <list type="number">
    ///   <item>If target is a <see cref="Land"/> on the Battlefield →
    ///     destroy via <see cref="ZoneMoveReason.DestroyNoRegeneration"/>
    ///     (CR 701.7 + "it can't be regenerated" rider).</item>
    ///   <item>Else if target is a <see cref="Creature"/> on the Battlefield
    ///     AND has no <see cref="ManaColor.Black"/> pip (CR 105) →
    ///     destroy via <see cref="ZoneMoveReason.DestroyNoRegeneration"/>.</item>
    ///   <item>Else → no-op (CR 608.2b — invalid target at resolution).</item>
    /// </list>
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that supply
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
                    Description: "target land or nonblack creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: gather every land + every nonblack
                    // creature on any battlefield. Removal intent pushes
                    // the opponent's most relevant permanent to the top.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c is Land ||
                                    (c is Creature cr &&
                                     !CardColors.GetColors(cr).Contains(ManaColor.Black)))
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
                        $"{CardName}: destroy target land or nonblack creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // Branch 1: target is a Land on the battlefield.
                            if (resolved is Land land)
                            {
                                if (land.Zone != ZoneType.Battlefield) return;
                                // CR 701.7 + "it can't be regenerated" rider.
                                OracleSpellBinder.MoveToGraveyard(
                                    land,
                                    ZoneMoveReason.DestroyNoRegeneration);
                                return;
                            }

                            // Branch 2: target is a nonblack creature on the battlefield.
                            if (resolved is Creature creature)
                            {
                                if (creature.Zone != ZoneType.Battlefield) return;
                                // CR 105 — colour from mana cost pips; nonblack = no {B} pip.
                                if (CardColors.GetColors(creature).Contains(ManaColor.Black)) return;
                                // CR 701.7 + "it can't be regenerated" rider.
                                OracleSpellBinder.MoveToGraveyard(
                                    creature,
                                    ZoneMoveReason.DestroyNoRegeneration);
                                return;
                            }

                            // Otherwise: illegal target at resolution — no-op (CR 608.2b).
                        }),
                };
            });
    }
}
