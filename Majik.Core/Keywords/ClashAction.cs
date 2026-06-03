using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.32 — Clash. Two players (the initiator and one opponent, for the
/// "clash with an opponent" cards in Modern) each reveal the top card of their
/// library, then each independently chooses to put that card on the top or
/// bottom of their library. A player "wins the clash" if their revealed card
/// has a greater mana value than the other revealed card.
///
/// <para>
/// CR 701.32a — "To clash, a player reveals the top card of their library, and
/// then any other player who is participating in the clash does the same. If
/// there are no cards in a player's library, that player reveals no cards. A
/// player wins the clash if that player revealed a card with a higher mana
/// value than each other card revealed this way."
/// CR 701.32b — "After the clash, each player who revealed a card leaves that
/// card on top of their library or puts it on the bottom. Each such player
/// makes this choice for the card they revealed. These choices are made and
/// applied simultaneously."
/// CR 701.32c — the top/bottom choice (consults each player's registered
/// <see cref="IPlayerAgent"/> via
/// <see cref="IPlayerAgent.ChooseClashTopOrBottomAsync"/>; default keeps on
/// top — the library-preserving posture).
/// CR 701.32d — "A clash isn't a contest, and it isn't combat. Casting a spell
/// or activating an ability during a clash doesn't cause anything to win or
/// lose the clash."
/// </para>
///
/// <para>
/// This is the multi-player simultaneous-reveal + mana-value-comparison
/// primitive. It returns a <see cref="ClashResult"/> token carrying which
/// player(s) won, so a card's follow-up clause ("If you win, …") can branch
/// declaratively off the result. An empty library reveals no card, which is
/// treated as mana value 0 for the comparison (a real card always has mana
/// value &gt;= 0, so an empty-library player can only tie another empty
/// library — never win).
/// </para>
///
/// <para>
/// CR 202.3b — "mana value" of the revealed card is read off its printed mana
/// cost (cards in the library have no chosen X / no cost modifiers).
/// </para>
/// </summary>
public static class ClashAction
{
    /// <summary>
    /// Resolve a two-player clash (CR 701.32) between
    /// <paramref name="initiator"/> (the controller of the clashing card —
    /// the "you" in "if you win") and <paramref name="other"/> (the chosen
    /// opponent). Reveals the top card of each library, prompts each player's
    /// top-or-bottom choice, applies the moves, and returns the
    /// <see cref="ClashResult"/>.
    /// </summary>
    /// <param name="initiator">The player initiating the clash — the "you" a
    /// follow-up "if you win the clash" clause resolves for.</param>
    /// <param name="other">The opponent participating in the clash.</param>
    /// <param name="initiatorAgent">Agent consulted for the initiator's
    /// top-or-bottom choice; falls back to <see cref="AgentRegistry"/> then
    /// keep-on-top.</param>
    /// <param name="otherAgent">Agent consulted for the opponent's
    /// top-or-bottom choice; same fallback chain.</param>
    /// <param name="game">Live game context passed to the agent prompts
    /// (nullable on the v1 sync-over-async closure path).</param>
    public static async ValueTask<ClashResult> ClashAsync(
        Player initiator,
        Player other,
        IPlayerAgent? initiatorAgent,
        IPlayerAgent? otherAgent,
        Majik.Core.Game.GameContext? game,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initiator);
        ArgumentNullException.ThrowIfNull(other);

        // CR 701.32a — each participating player reveals the top card of their
        // library (an empty library reveals nothing). Both reveals happen
        // before either player chooses top/bottom (CR 701.32b — choices are
        // applied simultaneously), so peek both first, then prompt, then move.
        var initiatorCard = initiator.Zones.Library.GetCards().FirstOrDefault();
        var otherCard = other.Zones.Library.GetCards().FirstOrDefault();

        var initiatorMv = ManaValueOf(initiatorCard);
        var otherMv = ManaValueOf(otherCard);

        // CR 701.32a — a player wins if their card had a GREATER mana value
        // (strictly greater; a tie wins for neither). With exactly two
        // participants this reduces to a pairwise comparison.
        var initiatorWon = initiatorCard is not null && initiatorMv > otherMv;
        var otherWon = otherCard is not null && otherMv > initiatorMv;

        // CR 701.32b/c — each player who revealed a card chooses top or bottom.
        await ApplyTopOrBottomChoiceAsync(initiator, initiatorCard, initiatorAgent, game, ct)
            .ConfigureAwait(false);
        await ApplyTopOrBottomChoiceAsync(other, otherCard, otherAgent, game, ct)
            .ConfigureAwait(false);

        return new ClashResult(
            Initiator: initiator,
            Other: other,
            InitiatorRevealed: initiatorCard,
            OtherRevealed: otherCard,
            InitiatorManaValue: initiatorMv,
            OtherManaValue: otherMv,
            InitiatorWon: initiatorWon,
            OtherWon: otherWon);
    }

    private static async ValueTask ApplyTopOrBottomChoiceAsync(
        Player player,
        ICard? revealed,
        IPlayerAgent? agent,
        Majik.Core.Game.GameContext? game,
        CancellationToken ct)
    {
        // CR 701.32a — no card revealed (empty library) → no choice to make.
        if (revealed is null) return;

        var chooser = agent ?? AgentRegistry.Get(player);
        var keepOnTop = chooser is null
            ? true // No agent — keep on top (library-preserving default).
            : await chooser.ChooseClashTopOrBottomAsync(game, revealed, ct)
                .ConfigureAwait(false);

        // keepOnTop: the card is already on top of the library — no move.
        if (keepOnTop) return;

        // CR 701.32b — put the revealed card on the bottom of the library.
        // Library index 0 == top, last index == bottom (mirrors ExploreAction's
        // FirstOrDefault top-read). Re-adding moves it to the end (bottom).
        player.Zones.Library.RemoveCard(revealed);
        player.Zones.Library.AddCard(revealed);
    }

    /// <summary>
    /// CR 202.3b — mana value of the revealed card, read off its printed mana
    /// cost. A null card (empty library, CR 701.32a) has mana value 0.
    /// </summary>
    private static int ManaValueOf(ICard? card)
    {
        if (card is null) return 0;
        if (card is Card concrete) return concrete.ManaCostValue.TotalValue;
        return Majik.Core.ValueObjects.ManaCost.Parse(card.ManaCost).TotalValue;
    }
}
