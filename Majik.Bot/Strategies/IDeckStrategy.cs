using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Strategies;

/// <summary>
/// Per-deck strategic knowledge distilled from the deck's primer. Resolved by
/// archetype name. Fair decks need no implementation (null → unchanged behavior).
/// </summary>
public interface IDeckStrategy
{
    /// Advisory eval bonus folded into BoardEval — values progress toward the
    /// deck's plan (combo pieces assembled, payoff protected, ramp toward finisher).
    double StrategicScore(GameContext ctx, Player self);

    /// Directive: the NEXT action of an ASSEMBLED win-line (else null). Checked
    /// first in PickPriorityAction; re-evaluated each priority window.
    PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self);

    /// Advisory: the primer's keep/ship rule (else null → generic MulliganPolicy).
    MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int mulligansTaken);

    /// <summary>
    /// Card names that this strategy explicitly references — key pieces the
    /// strategy logic depends on (combo components, finishers, tutor targets).
    /// Populated by strategy authors to document key cards AND to enable the
    /// deck-strategy coverage tripwire to validate that every referenced name
    /// exists in the archetype's <see cref="Majik.Bot.Decks.BotDeckCatalog"/>
    /// card list. Default: empty (strategy references no specific card names).
    /// </summary>
    IReadOnlyList<string> ReferencedCardNames => Array.Empty<string>();
}
