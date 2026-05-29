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
/// Named-card factory for Vandalblast (Return to Ravnica, {R}).
///
/// Sorcery. Oracle text:
///   "Destroy target artifact you don't control.
///    Overload {4}{R} (You may cast this spell for its overload cost. If you
///    do, change "target" in its text to "each.")"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Destroy each artifact you don't control."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {R}, Red.
/// - <b>Destroy target artifact you don't control</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target artifact you don't control"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding permanents that have type Artifact
///   (CR 301) and that the spell's controller does NOT control (CR 109.5 —
///   "you" = the spell's controller).
/// - On resolution (default branch): re-checks the target is still a
///   <see cref="Permanent"/> on the Battlefield (CR 608.2b illegal-target
///   gate), is an Artifact, and is NOT controlled by the spell's controller;
///   then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7). Indestructible
///   (CR 702.12) and regeneration shields (CR 701.15) are honoured by the
///   Destroy-reason gate — same posture as <see cref="SmeltFactory"/> /
///   <see cref="ShatterFactory"/>.
///
/// ## Overload (CR 702.96 — structural-flag-only, mirrors Mizzium Mortars)
///
/// Overload is an alternative cost. The
/// <see cref="Majik.Core.Costs.OverloadAlternativeCost"/> primitive (per
/// <c>MODERN_COVERAGE.md</c>) is a stub: it gates the cast and carries an
/// <c>IsOverloaded</c> flag, but is not yet plumbed through
/// <see cref="Majik.Core.Services.SpellCastFlow"/>'s payment loop, so the
/// "was overloaded?" bit does not flow from cast-time to the resolving stack
/// object. Until that infra lands, Vandalblast ships with
/// default-not-overloaded behaviour: cast resolves as "destroy target
/// artifact you don't control". The overloaded branch is structural — callers
/// can opt in via <c>wasOverloaded: true</c> on
/// <see cref="BuildDefinition"/>, which drops the target request and destroys
/// each artifact the controller does NOT control (CR 702.96b "target" →
/// "each" rewrite over "each artifact you don't control"). This is the same
/// posture as <see cref="MizziumMortarsFactory"/>.
///
/// ## CR notes
/// - CR 702.96 / 702.96b — Overload alt-cost; "target" → "each" rewrite.
/// - CR 109.5 — "you" in an object's text refers to that object's
///   controller; "you don't control" therefore excludes the spell
///   controller's own artifacts.
/// - CR 701.7 — Destroy; CR 702.12 indestructible / CR 701.15 regeneration
///   honoured at the destroy site.
/// - CR 608.2b — resolution-time legality re-check (still on battlefield,
///   still an artifact, still not controlled by the spell controller).
/// </summary>
[CardName("Vandalblast")]
public static class VandalblastFactory
{
    public const string CardName = "Vandalblast";
    public const string PrintedManaCost = "{R}";
    public const string OverloadCostText = "{4}{R}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (destroy target artifact you don't control) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the Vandalblast <see cref="SpellDefinition"/>.
    ///
    /// Default (<paramref name="wasOverloaded"/> = false): single 1..1
    /// "target artifact you don't control" request. The candidate gatherer
    /// walks every battlefield and yields artifacts the
    /// <paramref name="controller"/> does NOT control (CR 109.5). On resolve,
    /// validates the target is still a Permanent on the Battlefield, is an
    /// Artifact, and is not controlled by <paramref name="controller"/>
    /// (CR 608.2b), then destroys it (CR 701.7).
    ///
    /// Overloaded (<paramref name="wasOverloaded"/> = true): no target
    /// request; on resolve destroys every artifact the
    /// <paramref name="controller"/> does NOT control across
    /// <paramref name="allPlayers"/> (CR 702.96b).
    /// </summary>
    /// <param name="controller">The spell's controller — the "you" in
    /// "you don't control" (CR 109.5).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    /// <param name="allPlayers">All players whose battlefields the overloaded
    /// sweep should reach. Optional for the default branch; required for the
    /// overloaded sweep. Defaults to a single-element list of
    /// <paramref name="controller"/> (which, being the controller, yields no
    /// "you don't control" artifacts) when omitted.</param>
    /// <param name="wasOverloaded">Whether the overload alt-cost was paid at
    /// cast time. Defaults to <c>false</c> — overload is not yet wired through
    /// <see cref="Majik.Core.Services.SpellCastFlow"/>.</param>
    public static SpellDefinition BuildDefinition(
        Player controller,
        Func<object, object> targetResolver,
        IReadOnlyList<Player>? allPlayers = null,
        bool wasOverloaded = false)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(targetResolver);

        var players = allPlayers ?? new[] { controller };

        if (wasOverloaded)
        {
            // CR 702.96b — overloaded branch. "target" rewritten to "each":
            // destroy each artifact the controller does NOT control. Snapshot
            // the per-player artifact list before applying so same-step zone
            // moves don't disturb enumeration.
            return new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect(
                        $"{CardName} (overloaded): destroy each artifact you don't control.",
                        () =>
                        {
                            var seen = new HashSet<Permanent>();
                            foreach (var pl in players)
                            {
                                if (ReferenceEquals(pl, controller)) continue;
                                foreach (var p in pl.Zones.Battlefield.GetCards()
                                             .OfType<Permanent>()
                                             .Where(c => c.HasType(CardType.Artifact))
                                             .ToList())
                                {
                                    // CR 109.5 — only artifacts the controller
                                    // does NOT control.
                                    if (ReferenceEquals(p.Controller, controller)) continue;
                                    if (!seen.Add(p)) continue;
                                    // CR 701.7 — Destroy (indestructible /
                                    // regeneration honoured by MoveToGraveyard).
                                    OracleSpellBinder.MoveToGraveyard(
                                        p, ZoneMoveReason.Destroy);
                                }
                            }
                        }),
                });
        }

        // Default printed cast — single 1..1 "target artifact you don't
        // control" request; resolve = destroy that artifact.
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact you don't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield artifacts
                    // (CR 301) the spell controller does NOT control (CR 109.5).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact))
                        .OfType<Permanent>()
                        .Where(c => !ReferenceEquals(c.Controller, controller))
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
                        $"{CardName}: destroy target artifact you don't control.",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // Oracle constraint: must be an artifact (CR 608.2b).
                            if (!target.HasType(CardType.Artifact)) return;
                            // CR 109.5 — must not be controlled by the spell's
                            // controller ("you don't control").
                            if (ReferenceEquals(target.Controller, controller)) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target, ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
