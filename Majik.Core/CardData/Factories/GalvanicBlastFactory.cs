using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Galvanic Blast (Scars of Mirrodin, {R}).
///
/// Instant. Oracle text:
///   "Galvanic Blast deals 2 damage to any target.
///    Metalcraft — Galvanic Blast deals 4 damage to that target instead
///    if you control three or more artifacts."
///
/// ## Implementation
///
/// Plain "any target" burn at {R} with a Metalcraft (CR 702.95)
/// conditional upgrade. Resolve body counts the controller's
/// battlefield-artifact total at resolution time
/// (<see cref="MasterOfEtheriumFactory.CountArtifactsControlled"/> —
/// the canonical artifact-count helper shared with Master of Etherium /
/// Ardent Recruit / Mox Opal / Etched Champion) and picks
/// <see cref="KickedDamage"/> (4) when the controller controls
/// <see cref="MetalcraftThreshold"/> (3) or more artifacts, else
/// <see cref="BaseDamage"/> (2).
///
/// Damage is routed through <see cref="Fx.DealDamageAny"/> so all three
/// "any target" classes resolve correctly (CR 115.3 — Player / Creature /
/// Planeswalker; CR 306.7 — planeswalker damage becomes loyalty removal),
/// matching the resolve shape used by <see cref="ShockFactory"/> and
/// <see cref="BurstLightningFactory"/>'s base branch.
///
/// ## Metalcraft semantics
///
/// - "you control" reads the spell's controller, NOT the target's
///   controller (CR 109.5). The artifact count is taken from
///   <c>controller.Zones.Battlefield</c>.
/// - The threshold is checked at resolution (CR 608.2 — resolve body
///   re-reads game state when it cares about it). If the controller's
///   artifact count is bumped or dropped between cast and resolve (via
///   tapping a Mox to cast, or losing a Disciple to removal in
///   response), the resolve body sees the live count.
/// - Galvanic Blast itself is on the stack, not the battlefield, so it
///   does NOT count toward Metalcraft. The threshold therefore requires
///   3 other artifacts the controller controls.
/// - Opponent's artifacts do not contribute (CR 109.5 — controller-only).
///   This mirrors Etched Champion / Mox Opal / Ardent Recruit's
///   Metalcraft-counting convention.
/// </summary>
[CardName("Galvanic Blast")]
public static class GalvanicBlastFactory
{
    public const string CardName = "Galvanic Blast";
    public const string PrintedManaCost = "{R}";

    public const int BaseDamage = 2;
    public const int KickedDamage = 4;
    public const int MetalcraftThreshold = 3;

    /// <summary>CardDef DSL — card shape only. Metalcraft-conditional
    /// damage body is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Galvanic Blast
    /// is cast. Single 1..1 "any target" request; on resolution counts
    /// the controller's battlefield artifacts and deals
    /// <see cref="KickedDamage"/> (4) at Metalcraft threshold,
    /// <see cref="BaseDamage"/> (2) otherwise.
    /// </summary>
    /// <param name="controller">Spell controller — the player whose
    /// battlefield artifacts gate Metalcraft.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Galvanic Blast: Metalcraft-conditional damage", () =>
                    {
                        // CR 608.2 — resolve body re-reads state at
                        // resolution time. Live count off the
                        // controller's battlefield ensures Metalcraft
                        // reflects any cast-time / response-window
                        // changes (Mox taps, removal on Disciple).
                        var amount = MetalcraftActive(controller)
                            ? KickedDamage
                            : BaseDamage;
                        Fx.DealDamageAny(target, amount);
                    }),
                };
            });
    }

    /// <summary>
    /// CR 702.95 — Metalcraft predicate: true while
    /// <paramref name="controller"/> controls
    /// <see cref="MetalcraftThreshold"/> (3) or more artifacts on the
    /// battlefield. Delegates to
    /// <see cref="MasterOfEtheriumFactory.CountArtifactsControlled"/>
    /// (the shared "artifacts you control" helper used by Master of
    /// Etherium / Ardent Recruit / Mox Opal / Etched Champion).
    /// </summary>
    public static bool MetalcraftActive(Player controller)
        => MasterOfEtheriumFactory.CountArtifactsControlled(controller)
            >= MetalcraftThreshold;
}
