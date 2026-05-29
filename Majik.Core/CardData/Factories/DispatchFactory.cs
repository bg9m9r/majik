using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dispatch (New Phyrexia, {W}).
///
/// Instant. Oracle text:
///   "Tap target creature.
///    Metalcraft — If you control three or more artifacts, exile that
///    creature."
///
/// ## Implementation
///
/// A single 1..1 "target creature" request. On resolution the targeted
/// creature is tapped unconditionally (CR 701.21), then — if Metalcraft is
/// active (CR 702.95 — the spell's controller controls three or more
/// artifacts) — the same creature is exiled (CR 701.20). Both clauses act
/// on "that creature", i.e. the single chosen target, in printed-text order
/// inside one resolution (CR 608.2c).
///
/// Models the same two building blocks as the suggested analogues:
///   - Metalcraft gate, identical to <see cref="GalvanicBlastFactory"/> /
///     <see cref="EtchedChampionFactory"/> — delegated to
///     <see cref="MasterOfEtheriumFactory.CountArtifactsControlled"/>, the
///     shared "artifacts you control" helper.
///   - Exile-target-creature resolve body, identical in shape to
///     <see cref="PathToExileFactory"/> / <see cref="FellFactory"/>.
///
/// ## Metalcraft semantics
///
/// - "you control" reads the spell's controller, NOT the target creature's
///   controller (CR 109.5). The artifact count is taken from the
///   controller's battlefield.
/// - The threshold is checked at resolution (CR 608.2 — the resolve body
///   re-reads game state). If the controller's artifact count is bumped or
///   dropped between cast and resolve, the resolve body sees the live count.
/// - Dispatch itself is an Instant on the stack, not an artifact on the
///   battlefield, so it does NOT count toward Metalcraft. The threshold
///   therefore requires 3 artifacts the controller already controls.
/// - Opponent's artifacts do not contribute (CR 109.5 — controller-only),
///   mirroring Galvanic Blast / Etched Champion / Mox Opal.
///
/// ## Resolution-time legality (CR 608.2b)
///
/// If the chosen target is no longer a creature on the battlefield at
/// resolution (left in response), Dispatch does nothing — neither tap nor
/// exile fires. The tap clause and the Metalcraft exile both guard on the
/// target still being a battlefield creature.
/// </summary>
[CardName("Dispatch")]
public static class DispatchFactory
{
    public const string CardName = "Dispatch";
    public const string PrintedManaCost = "{W}";
    public const int MetalcraftThreshold = 3;

    /// <summary>CardDef DSL — card shape only. The tap + conditional-exile
    /// body lives in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Dispatch is cast.
    /// Single 1..1 "target creature" request; on resolution the targeted
    /// creature is tapped (CR 701.21) and, when the controller has
    /// Metalcraft (CR 702.95), exiled (CR 701.20).
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
                    new Effect($"{CardName}: tap target creature + Metalcraft exile", () =>
                    {
                        if (raw is not Creature target) return;

                        // CR 608.2b — resolution-time legality. Target must
                        // still be a creature on the battlefield.
                        if (target.Zone != ZoneType.Battlefield) return;

                        // CR 701.21 — Tap target creature (unconditional).
                        Fx.Tap(target);

                        // Metalcraft — CR 702.95. "you" is the spell's
                        // controller (CR 109.5), counted live at resolution
                        // (CR 608.2). When active, exile that same creature
                        // (CR 701.20).
                        if (MetalcraftActive(controller))
                        {
                            Fx.MoveToExile(target);
                        }
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
    /// (the shared "artifacts you control" helper used by Galvanic Blast /
    /// Etched Champion / Mox Opal / Ardent Recruit).
    /// </summary>
    public static bool MetalcraftActive(Player controller)
        => MasterOfEtheriumFactory.CountArtifactsControlled(controller)
            >= MetalcraftThreshold;
}
