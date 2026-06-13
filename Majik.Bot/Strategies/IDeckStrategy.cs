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

    /// <summary>
    /// Directive override — return the next action ONLY when an ATOMIC,
    /// (near-)immediate KILL is assembled (e.g. cast the lethal spell, the
    /// storm payoff, the one-shot combo that wins THIS turn).
    ///
    /// <para>For multi-turn ENGINES / value combos (reanimator,
    /// ramp-to-payoff), return <see langword="null"/> and rely on
    /// <see cref="StrategicScore"/> to steer the search instead — directive
    /// override of the search hurts on non-atomic lines (measured: it
    /// over-commits and loses, e.g. GrixisReanimator 20 % win-rate when
    /// directive-forced vs 12/16 combo fires).</para>
    ///
    /// <para>Checked first in PickPriorityAction; re-evaluated each priority
    /// window.</para>
    /// </summary>
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
