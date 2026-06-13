using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Strategies;

/// <summary>
/// No-op <see cref="IDeckStrategy"/> used as a sentinel to force the OFF seat
/// in controlled deck-strategy lift probes.
///
/// <para>
/// Passing this as the <c>deckOverride</c> to <see cref="Search.SearchStrategy"/>'s
/// internal test-seam constructor prevents the registry from auto-resolving the
/// real deck strategy for the archetype (because the ternary is
/// <c>deckOverride ?? DeckStrategyRegistry.For(...)</c> — a non-null override
/// short-circuits registry lookup). All methods return neutral / zero values so
/// the deck strategy contributes nothing to eval or action selection: the only
/// difference between the ON and OFF seats is the presence of the real strategy.
/// </para>
/// </summary>
internal sealed class NullDeckStrategy : IDeckStrategy
{
    /// <summary>
    /// No strategic score bonus — contributes zero to MCTS eval.
    /// </summary>
    public double StrategicScore(GameContext ctx, Player self) => 0.0;

    /// <summary>
    /// No assembled win-line — never overrides MCTS / heuristic action selection.
    /// </summary>
    public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self) => null;

    /// <summary>
    /// No deck-specific mulligan advice — generic mulligan policy applies.
    /// </summary>
    public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int mulligansTaken) => null;
}
