using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cyclonic Rift (Return to Ravnica, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-02):
///   "Return target nonland permanent you don't control to its owner's hand.
///    Overload {6}{U} (You may cast this spell for its overload cost. If you
///    do, change "target" in its text to "each.")"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Return each nonland permanent you don't control to its owner's hand."
///
/// Cyclonic Rift is the bounce analogue of <see cref="VandalblastFactory"/>
/// (which <i>destroys</i> "target artifact you don't control" with the same
/// overload "target" → "each" rewrite): the per-permanent effect is a
/// return-to-owner's-hand bounce (cf. <see cref="EchoingTruthFactory"/>,
/// <see cref="BoomerangFactory"/>) rather than a destroy, and the candidate
/// pool is every nonland permanent the controller does NOT control
/// (CR 109.5 — "you" = the spell's controller).
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {1}{U}, Blue. The base shape
///   (name / Instant type / {1}{U} cost) is materialised from the embedded
///   JSON definition (<c>cyclonic-rift.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="EchoingTruthFactory"/> (the JSON <c>SpellDefinition</c>
///   schema does not yet express a nonland-permanent target request or the
///   overload sweep, so the resolve behaviour is layered on here via
///   <see cref="BuildDefinition"/>).
/// - <b>Return target nonland permanent you don't control</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target nonland permanent you don't control"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding permanents whose card-type set
///   does NOT include <see cref="CardType.Land"/> (CR 305 — Land is a card
///   type) and that the spell's controller does NOT control (CR 109.5).
/// - On resolution (default branch): re-checks the target is still a
///   <see cref="Permanent"/> on the Battlefield (CR 608.2b illegal-target
///   gate), is nonland, and is NOT controlled by the spell's controller;
///   then returns it to its owner's hand (CR 701.20).
///
/// ## Overload (CR 702.96 — structural-flag-only, mirrors Vandalblast)
///
/// Overload is an alternative cost. The
/// <see cref="Majik.Core.Costs.OverloadAlternativeCost"/> primitive (per
/// <c>MODERN_COVERAGE.md</c>) is a stub: it gates the cast and carries an
/// <c>IsOverloaded</c> flag, but is not yet plumbed through
/// <see cref="Majik.Core.Services.SpellCastFlow"/>'s payment loop, so the
/// "was overloaded?" bit does not flow from cast-time to the resolving stack
/// object. Until that infra lands, Cyclonic Rift ships with
/// default-not-overloaded behaviour: the cast resolves as "return target
/// nonland permanent you don't control to its owner's hand". The overloaded
/// branch is structural — callers can opt in via <c>wasOverloaded: true</c>
/// on <see cref="BuildDefinition"/>, which drops the target request and
/// bounces each nonland permanent the controller does NOT control
/// (CR 702.96b "target" → "each" rewrite). Same posture as
/// <see cref="VandalblastFactory"/> / <see cref="MizziumMortarsFactory"/>.
///
/// ## CR notes
/// - CR 702.96 / 702.96b — Overload alt-cost; "target" → "each" rewrite.
/// - CR 109.5 — "you" in an object's text refers to that object's
///   controller; "you don't control" therefore excludes the spell
///   controller's own permanents.
/// - CR 305 — Land is a card type; "nonland permanent" excludes lands.
/// - CR 701.20 — return to owner's hand.
/// - CR 608.2b — resolution-time legality re-check (still on battlefield,
///   still nonland, still not controlled by the spell controller).
/// </summary>
[CardName("Cyclonic Rift")]
public static class CyclonicRiftFactory
{
    public const string CardName = "Cyclonic Rift";
    public const string PrintedManaCost = "{1}{U}";
    public const string OverloadCostText = "{6}{U}";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "cyclonic-rift";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {1}{U}) from the
    /// embedded JSON definition. Resolve behaviour (return target nonland
    /// permanent you don't control to its owner's hand) is built on demand
    /// via <see cref="BuildDefinition"/>, mirroring
    /// <see cref="EchoingTruthFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the Cyclonic Rift <see cref="SpellDefinition"/>.
    ///
    /// Default (<paramref name="wasOverloaded"/> = false): single 1..1
    /// "target nonland permanent you don't control" request. The candidate
    /// gatherer walks every battlefield and yields nonland permanents the
    /// <paramref name="controller"/> does NOT control (CR 109.5 / CR 305).
    /// On resolve, validates the target is still a Permanent on the
    /// Battlefield, is nonland, and is not controlled by
    /// <paramref name="controller"/> (CR 608.2b), then returns it to its
    /// owner's hand (CR 701.20).
    ///
    /// Overloaded (<paramref name="wasOverloaded"/> = true): no target
    /// request; on resolve bounces every nonland permanent the
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
    /// "you don't control" permanents) when omitted.</param>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves. When null, raw zone manipulation is used (mirrors
    /// <see cref="EchoingTruthFactory"/>).</param>
    /// <param name="wasOverloaded">Whether the overload alt-cost was paid at
    /// cast time. Defaults to <c>false</c> — overload is not yet wired through
    /// <see cref="Majik.Core.Services.SpellCastFlow"/>.</param>
    public static SpellDefinition BuildDefinition(
        Player controller,
        Func<object, object> targetResolver,
        IReadOnlyList<Player>? allPlayers = null,
        ZoneService? zoneService = null,
        bool wasOverloaded = false)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(targetResolver);

        var players = allPlayers ?? new[] { controller };

        if (wasOverloaded)
        {
            // CR 702.96b — overloaded branch. "target" rewritten to "each":
            // return each nonland permanent the controller does NOT control
            // to its owner's hand. Snapshot the per-player permanent list
            // before applying so same-step zone moves don't disturb
            // enumeration.
            return new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: p =>
                {
                    var sweepPlayers = p.AllPlayers ?? players;
                    return new IEffect[]
                    {
                        new Effect(
                            $"{CardName} (overloaded): return each nonland permanent you don't control to its owner's hand.",
                            () =>
                            {
                                var seen = new HashSet<Permanent>();
                                foreach (var pl in sweepPlayers)
                                {
                                    if (ReferenceEquals(pl, controller)) continue;
                                    foreach (var perm in pl.Zones.Battlefield.GetCards()
                                                 .OfType<Permanent>()
                                                 .Where(c => !c.HasType(CardType.Land))
                                                 .ToList())
                                    {
                                        // CR 109.5 — only permanents the
                                        // controller does NOT control.
                                        if (ReferenceEquals(perm.Controller, controller)) continue;
                                        if (!seen.Add(perm)) continue;
                                        // CR 608.2b — guard against a same-step
                                        // move having already pulled this
                                        // permanent off the battlefield.
                                        if (perm.Zone != ZoneType.Battlefield) continue;
                                        // CR 701.20 — return to owner's hand.
                                        ReturnToOwnersHand(perm, zoneService);
                                    }
                                }
                            }),
                    };
                });
        }

        // Default printed cast — single 1..1 "target nonland permanent you
        // don't control" request; resolve = bounce that permanent.
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonland permanent you don't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // Agent-prompt: walk every battlefield, yield nonland
                    // permanents (CR 305) the spell controller does NOT
                    // control (CR 109.5).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => !c.HasType(CardType.Land))
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
                        $"{CardName}: return target nonland permanent you don't control to its owner's hand.",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 305 — must be a nonland permanent.
                            if (target.HasType(CardType.Land)) return;
                            // CR 109.5 — must not be controlled by the spell's
                            // controller ("you don't control").
                            if (ReferenceEquals(target.Controller, controller)) return;

                            // CR 701.20 — return to owner's hand.
                            ReturnToOwnersHand(target, zoneService);
                        }),
                };
            });
    }

    /// <summary>
    /// CR 701.20 — return a single permanent to its owner's hand. When a
    /// <see cref="ZoneService"/> is supplied the move is routed through it so
    /// replacement effects / zone-change events fire; otherwise raw zone
    /// manipulation is used (same posture as <see cref="EchoingTruthFactory"/>).
    /// </summary>
    private static void ReturnToOwnersHand(Permanent perm, ZoneService? zoneService)
    {
        var owner = perm.Owner;
        if (owner == null) return;

        var controller = perm.Controller ?? owner;

        if (zoneService != null)
        {
            zoneService.MoveCard(perm, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(perm);
            owner.Zones.Hand.AddCard(perm);
            perm.SetZone(ZoneType.Hand);
            perm.SetController(owner);
        }
    }
}
