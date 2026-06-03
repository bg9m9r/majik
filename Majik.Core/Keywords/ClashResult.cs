using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.32 — the result token of a two-player clash, returned by
/// <see cref="ClashAction.ClashAsync"/>. Carries each participant, their
/// revealed card + mana value, and which player(s) won, so a card's follow-up
/// clause ("If you win the clash, …" — Recross the Paths) can branch off the
/// outcome declaratively without re-deriving the comparison.
/// </summary>
/// <param name="Initiator">The player who initiated the clash — the "you" an
/// "if you win the clash" clause resolves for (CR 701.32a).</param>
/// <param name="Other">The opponent who participated in the clash.</param>
/// <param name="InitiatorRevealed">The card the initiator revealed off the top
/// of their library, or <see langword="null"/> if their library was empty
/// (CR 701.32a).</param>
/// <param name="OtherRevealed">The card the opponent revealed, or
/// <see langword="null"/> if their library was empty.</param>
/// <param name="InitiatorManaValue">CR 202.3b mana value of the initiator's
/// revealed card (0 when no card was revealed).</param>
/// <param name="OtherManaValue">Mana value of the opponent's revealed card
/// (0 when no card was revealed).</param>
/// <param name="InitiatorWon"><see langword="true"/> when the initiator's
/// revealed card had a strictly greater mana value than the opponent's
/// (CR 701.32a). A tie wins for neither player.</param>
/// <param name="OtherWon"><see langword="true"/> when the opponent's revealed
/// card had the strictly greater mana value.</param>
public sealed record ClashResult(
    Player Initiator,
    Player Other,
    ICard? InitiatorRevealed,
    ICard? OtherRevealed,
    int InitiatorManaValue,
    int OtherManaValue,
    bool InitiatorWon,
    bool OtherWon);
