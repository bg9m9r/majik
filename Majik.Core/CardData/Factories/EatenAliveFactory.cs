using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eaten Alive (Innistrad: Midnight Hunt, {B}).
///
/// Sorcery. Oracle text:
///   "As an additional cost to cast this spell, sacrifice a creature or
///    pay {3}{B}.
///    Exile target creature or planeswalker."
///
/// ## Why it gets its own factory
/// Eaten Alive is the EXILE sibling of <see cref="SparkHarvestFactory"/>:
/// identical printed cost ({B}), identical disjunctive additional cost
/// (sacrifice a creature OR pay {3}{B}), identical "target creature or
/// planeswalker" body — the only difference is the removal verb. Spark
/// Harvest DESTROYS (CR 701.7, regen/indestructible apply); Eaten Alive
/// EXILES (CR 701.21), which sidesteps both indestructible (CR 702.12) and
/// regeneration (CR 701.15) and removes the permanent from the game rather
/// than to its graveyard.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}, CMC 1, black.
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="SacrificeCreatureOrPayManaAdditionalCost"/> ({3}{B}) —
///   disjunctive payment that prefers sacrificing a creature when one is
///   available (v1 deterministic) and falls back to paying {3}{B}
///   otherwise. The cast flow's pre-check (<see cref="SpellCastFlow"/>)
///   rejects the cast when NEITHER mode is payable (CR 601.2g — additional
///   cost that can't be paid → cast is illegal). Shared, unchanged, with
///   Spark Harvest.
/// - <b>Exile target creature or planeswalker</b> —
///   <see cref="BuildDefinition"/> declares a single 1..1 "target creature
///   or planeswalker" <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Removal"/>) with a live
///   <see cref="TargetRequest.CandidateGatherer"/> that enumerates every
///   creature + planeswalker on the battlefield across all players. On
///   resolution the targeted permanent is exiled via
///   <see cref="Fx.MoveToExile(ICard)"/> (CR 701.21) iff it is still a
///   creature or planeswalker on the battlefield at resolution
///   (CR 608.2b — illegal target → no-op). Because this is exile, not
///   destroy, indestructible and regeneration shields do NOT save the
///   target.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven mode choice</b>: v1 picks sacrifice-first when both
///   modes are payable — same deferred-mode-prompt queue as the sibling
///   disjunctive costs (Spark Harvest, Bone Shards, Lightning Axe).
/// </summary>
[CardName("Eaten Alive")]
public static class EatenAliveFactory
{
    public const string CardName = "Eaten Alive";
    public const string PrintedManaCost = "{B}";

    /// <summary>The mana the pay-mana mode of the additional cost charges.</summary>
    public const string AdditionalPayManaCost = "{3}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour is built
    /// on demand via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Eaten Alive is
    /// cast. Declares the disjunctive sacrifice-creature-or-pay-{3}{B}
    /// additional cost (CR 601.2f) alongside a single 1..1 "target creature
    /// or planeswalker" <see cref="TargetRequest"/>; on resolution the
    /// targeted permanent is exiled (CR 701.21) iff it is still a creature
    /// or planeswalker on the battlefield at resolution (CR 608.2b).
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
                    // planeswalkers on the battlefield across every player —
                    // HeuristicBotAgent.Score handles the ownership flip so
                    // opponent permanents rank ahead of own permanents for
                    // Removal intent.
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
                        $"{CardName}: exile target creature or planeswalker",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // If the chosen target was the same creature the
                            // caster sacrificed for the additional cost, it
                            // has already moved to the graveyard and this
                            // guard makes the exile a no-op.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Planeswalker)) return;

                            // CR 701.21 — Exile. Unlike Spark Harvest's
                            // destroy, indestructible (CR 702.12) and
                            // regeneration (CR 701.15) do NOT prevent exile.
                            Fx.MoveToExile(target);
                        }),
                };
            },
            AdditionalCosts: new IAdditionalCost[]
            {
                new SacrificeCreatureOrPayManaAdditionalCost(
                    ManaCost.Parse(AdditionalPayManaCost)),
            });
    }
}
