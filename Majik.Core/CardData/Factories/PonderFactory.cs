using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

// Resolve the LibraryShuffle helper via fully-qualified name to keep the
// import set minimal.

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
///   cards back on top), then resolves the "you may shuffle your library"
///   rider via <see cref="IPlayerAgent.ChooseYesNoAsync"/> tagged with
///   <see cref="BotIntent.LibraryReorder"/> (CR 701.20 — shuffle routes
///   through <see cref="LibraryShuffle.ShuffleLibrary"/>), then draws a
///   card.
/// - With no agent registered, the default keeps the peeked cards in their
///   original order (pre-agent legacy behaviour — same shape as
///   <see cref="ConsiderFactory"/>'s default-surveil fallback) and the
///   yes/no shuffle prompt falls through the default heuristic accept
///   branch (auto-shuffle, matching the legacy posture).
/// - Empty library: the peek short-circuits and the subsequent draw flags
///   the player for the standard draw-from-empty-library penalty
///   (CR 704.5b) via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
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
    /// agent (or keep the original order if none), resolve the "you may
    /// shuffle your library" rider via <see cref="IPlayerAgent.ChooseYesNoAsync"/>
    /// tagged with <see cref="BotIntent.LibraryReorder"/> (CR 701.20a —
    /// shuffle routes through <see cref="LibraryShuffle.ShuffleLibrary"/>),
    /// then draw a card.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Ponder: look at top 3, reorder, may shuffle, draw 1.", async ctx =>
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
                    var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                    ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        // TODO: drop sync-over-async once IEffect.Execute becomes async.
                        var agentDecision = (await agent.ChooseScryDecisionAsync( ctx.Game, peeked).ConfigureAwait(false));
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

                // "You may shuffle your library." CR 701.20 — route the
                // yes/no through IPlayerAgent.ChooseYesNoAsync tagged with
                // BotIntent.LibraryReorder; on accept, the actual shuffle is
                // performed via LibraryShuffle.ShuffleLibrary so the
                // registered RNG + LibraryShuffledEvent fire (same path the
                // tutor factories use). With no agent registered the
                // IPlayerAgent default-heuristic accept branch fires, so
                // legacy callers get the historical auto-shuffle posture.
                var shuffleAgent = ctx.Agent ?? AgentRegistry.Get(caster);
                if (shuffleAgent != null)
                {
                    // TODO: drop sync-over-async once IEffect.Execute becomes async.
                    var shouldShuffle = (await shuffleAgent.ChooseYesNoAsync(
                        "Shuffle your library?",
                        BotIntent.LibraryReorder).ConfigureAwait(false));
                    if (shouldShuffle)
                    {
                        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "ponder");
                    }
                }
                // No agent registered → leave the (possibly reordered) top
                // three on top, mirroring the pre-agent legacy posture used
                // by every test fixture written before this prompt shipped.

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
