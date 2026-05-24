using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ponder (Lorwyn / Modern Horizons 3, {U}).
///
/// Sorcery. Oracle text:
///   "Look at the top three cards of your library, then put them back in any
///    order. You may shuffle your library. Draw a card."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) peeks up to three
///   cards off the top of the controller's library, asks the registered
///   <see cref="IPlayerAgent"/> for a reorder via <see cref="ScryAction"/>
///   semantics (<c>ToBottom</c> must be empty — Ponder puts all peeked
///   cards back on top), then draws a card.
/// - With no agent registered, the default keeps the peeked cards in their
///   original order (pre-agent legacy behaviour — same shape as
///   <see cref="ConsiderFactory"/>'s default-surveil fallback).
/// - Empty library: the peek short-circuits and the subsequent draw flags
///   the player for the standard draw-from-empty-library penalty
///   (CR 704.5b) via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
///
/// ## Deferred (v1 gaps)
/// - The "may shuffle your library" rider is a no-op in v1 — there is no
///   <c>IZone.Shuffle</c> entry point yet (same rationale as
///   <c>SearchSpellFactory</c>). The decision is not yet sourced from an
///   agent prompt.
/// </summary>
[CardName("Ponder")]
public static class PonderFactory
{
    public const string CardName = "Ponder";
    public const string PrintedManaCost = "{U}";

    /// <summary>CardDef DSL — card shape only. Resolve effect lives in
    /// <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Ponder's resolve effect — peek top 3, reorder via the registered
    /// agent (or keep the original order if none), then draw a card. The
    /// "may shuffle" rider is deferred (no-op).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Ponder: look at top 3, reorder, may shuffle, draw 1.", () =>
            {
                // Peek up to 3 cards. ScryAction.Peek tolerates short libraries
                // (returns up to N) so empty-library handling falls out for free.
                var peeked = ScryAction.Peek(caster, 3);
                if (peeked.Count > 0)
                {
                    // Reuse the ScryAction.Apply pipeline with ToBottom = [] —
                    // Ponder puts ALL peeked cards back on top in a chosen
                    // order. Sourced from the registered agent; the pre-agent
                    // default keeps the original order (TopOrder = peeked).
                    var agent = AgentRegistry.Get(caster);
                    ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        // TODO: drop sync-over-async once IEffect.Execute becomes async.
                        var agentDecision = agent.ChooseScryDecisionAsync(null, peeked)
                            .GetAwaiter().GetResult();
                        // Defensive: if an agent returns a non-empty ToBottom
                        // (Ponder is reorder-only, not partition), collapse it
                        // into TopOrder so the engine still puts everything
                        // back on top. Preserve the agent's relative ordering:
                        // its TopOrder first, then anything it tried to bottom.
                        if (agentDecision.ToBottom.Count > 0)
                        {
                            var collapsed = agentDecision.TopOrder
                                .Concat(agentDecision.ToBottom)
                                .ToList();
                            decision = new ScryAction.ScryDecision(
                                ToBottom: Array.Empty<ICard>(),
                                TopOrder: collapsed);
                        }
                        else
                        {
                            decision = agentDecision;
                        }
                    }
                    else
                    {
                        decision = new ScryAction.ScryDecision(
                            ToBottom: Array.Empty<ICard>(),
                            TopOrder: peeked.ToList());
                    }
                    ScryAction.Apply(caster, peeked.Count, decision);
                }

                // "You may shuffle your library." The IZone.Shuffle primitive
                // is now wired (CR 701.20), but Ponder's "may" rider is a
                // yes/no agent prompt — deferred behind the agent-prompt MVP
                // (rank #1 in MECHANIC_DEPS). v1 auto-declines, leaving the
                // (possibly reordered) top three on top.
                //
                // "Draw a card." Simple top-of-library draw; empty library
                // flags the player for the SBA-driven loss (CR 704.5b) via
                // MarkTriedToDrawFromEmptyLibrary.
                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    caster.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }),
        };
    }
}
