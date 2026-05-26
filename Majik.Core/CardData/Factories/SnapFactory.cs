using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snap (Urza's Saga, {U}{U}).
///
/// Instant. Oracle text:
///   "Return target creature to its owner's hand. Untap up to two lands."
///
/// ## Implemented (v1)
/// - Instant card shape ({U}{U}, Blue) — built via the fluent
///   <see cref="CardDef"/> DSL.
/// - Resolve effect (<see cref="BuildDefinition"/>):
///   1. Bounce target creature to its owner's hand via
///      <see cref="Fx.BounceToHand"/> (same shape Snapback / Unsummon use).
///      CR 608.2b — if the creature has moved off the battlefield by
///      resolution the bounce no-ops cleanly.
///   2. Untap up to two lands (any controller, any land) — the second
///      <see cref="TargetRequest"/> is open-cardinality (<c>MinTargets: 0,
///      MaxTargets: 2</c>); each chosen target that is still a Land
///      permanent on resolve is untapped via <see cref="Permanent.Untap"/>.
///      Same posture as the <c>UntapTargetTemplate</c>: defensive type +
///      tapped checks at resolve, no choose-time predicate plumbing.
///
/// ## Deferred (v1 gaps)
/// - <b>Target-land predicate at choose time</b>: the
///   <see cref="TargetRequest.LegalCandidates"/> is left empty (same posture
///   as Cryptic Command's bounce mode + the Untap template). The agent is
///   trusted to pick land permanents; non-land picks are filtered at
///   resolve. Tightens once we have a "target Land" predicate plumbed
///   through <see cref="TargetRequest"/>.
/// </summary>
[CardName("Snap")]
public static class SnapFactory
{
    public const string CardName = "Snap";
    public const string PrintedManaCost = "{U}{U}";

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "return target creature to its owner's hand; untap up to
    /// two lands" SpellDefinition.
    /// </summary>
    /// <param name="targetResolver">Resolves the chosen target objects
    /// (e.g. via <see cref="StackResolver"/>) for both target requests.</param>
    /// <param name="zoneService">Optional. Threads zone-move events for
    /// the bounce half. Shape-only callers pass null.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        ZoneService? zoneService = null) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // Slot 0 — the bounce target.
                new TargetRequest("target creature", 1, 1, Array.Empty<object>(), BotIntent.Bounce),
                // Slot 1 — up to two lands to untap (open-cardinality).
                new TargetRequest("up to two target lands", 0, 2, Array.Empty<object>(), BotIntent.Buff),
            },
            EffectFactory: p =>
            {
                var bounceRaw = p.Targets[0][0];
                var bounceTarget = targetResolver(bounceRaw);

                // Resolve the 0..2 land picks up-front so the effect closure
                // captures live references (same pattern Cryptic Command +
                // Kolaghan's Command use for their optional-slot picks).
                var landTargets = new List<object>(2);
                if (p.Targets.Count > 1)
                {
                    foreach (var raw in p.Targets[1])
                    {
                        landTargets.Add(targetResolver(raw));
                    }
                }

                return new IEffect[]
                {
                    new Effect("Snap — return target creature to its owner's hand", () =>
                    {
                        // CR 701.10 — return: source zone → owner's hand.
                        // CR 608.2b — illegal target (creature moved off the
                        // battlefield since cast) → no-op.
                        if (bounceTarget is not ICard card) return;
                        if (card.Owner == null) return;
                        if (card.Zone != ZoneType.Battlefield) return;
                        Fx.BounceToHand(card, zoneService);
                    }),
                    new Effect("Snap — untap up to two lands", () =>
                    {
                        foreach (var t in landTargets)
                        {
                            // CR 608.2b — defensive type + zone check at
                            // resolve. Non-land or off-battlefield picks
                            // are silently skipped.
                            if (t is not Permanent perm) continue;
                            if (perm.Zone != ZoneType.Battlefield) continue;
                            if (!perm.HasType(CardType.Land)) continue;
                            if (perm.IsTapped) perm.Untap();
                        }
                    }),
                };
            });
}
