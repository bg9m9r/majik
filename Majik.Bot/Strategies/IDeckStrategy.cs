using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Strategies;

/// <summary>
/// Per-deck strategic knowledge distilled from the deck's primer. Resolved by
/// archetype name. Fair decks need no implementation (null → unchanged behavior).
/// </summary>
internal interface IDeckStrategy
{
    /// Advisory eval bonus folded into BoardEval — values progress toward the
    /// deck's plan (combo pieces assembled, payoff protected, ramp toward finisher).
    double StrategicScore(GameContext ctx, Player self);

    /// Directive: the NEXT action of an ASSEMBLED win-line (else null). Checked
    /// first in PickPriorityAction; re-evaluated each priority window.
    PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self);

    /// Advisory: the primer's keep/ship rule (else null → generic MulliganPolicy).
    MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int mulligansTaken);
}
