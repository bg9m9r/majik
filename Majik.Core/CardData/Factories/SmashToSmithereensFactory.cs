using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Smash to Smithereens (Mirrodin, {1}{R}).
///
/// Instant. Oracle text:
///   "Destroy target artifact. It can't be regenerated. This deals 3
///    damage to that artifact's controller."
///
/// ## Why a named factory
/// Smash to Smithereens is the canonical "red artifact removal with a
/// face-damage rider" sideboard staple — two-mana instant-speed artifact
/// destruction that *also* punishes the artifact's controller for 3.
/// Same chassis as <see cref="EmberethShieldbreakerFactory"/>'s Battle
/// Display adventure half (destroy target artifact, CR 701.7) plus a
/// rider in the spirit of Shrapnel Blast — the 3 damage routes to the
/// destroyed artifact's controller (CR 119 + CR 109.2), making it a
/// uniquely "go-wide artifact deck punisher" out of Modern sideboards.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{R}.
/// - <b>Target</b>: single 1..1 "target artifact"
///   <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Removal"/>) — same shape as
///   <see cref="EmberethShieldbreakerFactory"/>'s Battle Display half.
/// - <b>Destroy (no-regen, CR 701.7 + CR 701.15c)</b>:
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
///   — indestructible (CR 702.12) still cancels the destroy, but any
///   active regeneration shield is bypassed. Mirrors
///   <see cref="TerminateFactory"/>'s no-regen posture.
/// - <b>3 damage to that artifact's controller (CR 119)</b>: captured
///   <em>before</em> the destroy so a freshly-graveyarded artifact's
///   Controller (now null / battlefield-cleared) doesn't elide the
///   damage rider. CR 121.x / 608.2c-style "last known information" is
///   the official anchor — the damage is dealt to whoever controlled
///   the artifact at the moment Smash to Smithereens resolved (CR
///   400.7a — last known information applies to objects that have
///   left the battlefield during resolution).
/// - <b>Illegal-target collapse</b>: if the chosen object is no longer
///   an artifact permanent on the battlefield at resolution time (CR
///   608.2b), the whole resolution is a clean no-op — no destroy AND
///   no 3-damage rider, mirroring Battle Display's "illegal target →
///   no-op" posture. The damage is gated on a successful destroy
///   because the printed text reads "that artifact's controller" —
///   without a live artifact target there's no anchor for the rider.
/// </summary>
[CardName("Smash to Smithereens")]
public static class SmashToSmithereensFactory
{
    public const string CardName = "Smash to Smithereens";
    public const string PrintedManaCost = "{1}{R}";

    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only. The destroy + damage
    /// body is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Smash to
    /// Smithereens is cast. Single 1..1 "target artifact" request; on
    /// resolution, destroys the targeted artifact (no-regen) and deals
    /// <see cref="Damage"/> (3) to its (last-known) controller.
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
                    "target artifact", 1, 1, Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target artifact + 3 damage to its controller",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality check.
                            // The target must still be an Artifact
                            // permanent on the battlefield; if not, the
                            // whole resolution collapses (no destroy, no
                            // damage rider — the printed "that artifact's
                            // controller" anchor doesn't exist).
                            if (raw is not Permanent permanent) return;
                            if (permanent.Zone != ZoneType.Battlefield) return;
                            if (!permanent.HasType(CardType.Artifact)) return;

                            // CR 400.7a / 608.2c — capture the
                            // artifact's controller BEFORE the destroy
                            // so the damage rider reads last-known
                            // information (the destroy clears the
                            // battlefield slot, after which
                            // permanent.Controller can become null).
                            var artifactController = permanent.Controller;

                            // CR 701.7 + CR 701.15c — Destroy. The
                            // printed "it can't be regenerated"
                            // suppresses the CR 701.15 regeneration
                            // shield. Indestructible (CR 702.12) still
                            // cancels the destroy — same posture as
                            // Terminate / Wrath of God.
                            OracleSpellBinder.MoveToGraveyard(
                                permanent,
                                Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);

                            // CR 119 — 3 damage to the artifact's
                            // controller. Fires unconditionally on a
                            // successful destroy (indestructible would
                            // have aborted MoveToGraveyard, but the
                            // printed rider on Smash to Smithereens
                            // reads as "deals 3 damage to that
                            // artifact's controller" regardless of
                            // whether the destroy actually graveyarded
                            // the permanent — modern Oracle templating
                            // ties the damage to the resolution itself,
                            // not to the destroy succeeding. Mirrors
                            // Searing Blood's "deals X damage" rider
                            // — the rider is independent of the
                            // companion clause).
                            if (artifactController != null)
                            {
                                Fx.DealDamage(artifactController, Damage);
                            }
                        }),
                };
            });
    }
}
