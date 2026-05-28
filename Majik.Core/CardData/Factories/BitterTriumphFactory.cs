using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bitter Triumph (The Lost Caverns of Ixalan,
/// {1}{B}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, discard a card or pay
///    3 life.
///    Destroy target creature or planeswalker."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, CMC 2, black.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="DiscardACardOrPayLifeAdditionalCost"/> — disjunctive
///   payment that prefers discarding a card when one is available (v1
///   deterministic) and falls back to paying 3 life otherwise. The cast
///   flow's pre-check (<see cref="SpellCastFlow"/>) rejects the cast
///   when NEITHER mode is payable (CR 601.2g — additional cost that
///   can't be paid → cast is illegal).
/// - <b>Destroy target creature or planeswalker</b> —
///   <see cref="BuildDefinition"/> declares a single 1..1 "target
///   creature or planeswalker" <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Removal"/>) with a live
///   <see cref="TargetRequest.CandidateGatherer"/> that enumerates
///   every creature + planeswalker on the battlefield across all players.
///   On resolution the targeted permanent is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7) iff it
///   is still a creature or planeswalker on the battlefield at resolution
///   (CR 608.2b — illegal target → no-op).
///
/// Indestructible (CR 702.12) cancels the destroy; active regeneration
/// shields (CR 701.15) are consumed via the Destroy reason. Bitter
/// Triumph does NOT print "can't be regenerated", so the regen shield
/// IS honoured.
///
/// ## Design references
/// - Additional-cost OR shape: <see cref="BoneShardsFactory"/> /
///   <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/> for the
///   disjunctive-cost pattern.
/// - Destroy creature or planeswalker: <see cref="HerosDownfallFactory"/>
///   for the destroy clause and candidate gatherer shape.
/// - Pay-life cost: <see cref="NecropotenceFactory"/> / <see cref="PayLifeCost"/>
///   for life-as-cost mechanics (CR 118.8 / 119.4).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven mode choice</b>: v1 picks discard-first when both
///   modes are payable. Full agent prompt ("would you rather discard a
///   card or pay 3 life?") is deferred — same queue as
///   <see cref="DiscardACardCost"/>'s discard-target prompt.
/// </summary>
[CardName("Bitter Triumph")]
public static class BitterTriumphFactory
{
    public const string CardName = "Bitter Triumph";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour is built
    /// on demand via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Bitter Triumph is
    /// cast. Declares the disjunctive discard-or-pay-life additional cost
    /// (CR 601.2f) alongside a single 1..1 "target creature or planeswalker"
    /// <see cref="TargetRequest"/>; on resolution the targeted permanent is
    /// destroyed (CR 701.7) iff it is still a creature or planeswalker on the
    /// battlefield at resolution (CR 608.2b).
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
                    Description: "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer (agent-prompt MVP). All creatures +
                    // planeswalkers on the battlefield across every
                    // player — HeuristicBotAgent.Score handles the
                    // ownership flip so opponent permanents rank ahead
                    // of own permanents for Removal intent.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
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
                        $"{CardName}: destroy target creature or planeswalker",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Planeswalker)) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.Destroy);
                        }),
                };
            },
            AdditionalCosts: new IAdditionalCost[]
            {
                new DiscardACardOrPayLifeAdditionalCost(),
            });
    }
}
