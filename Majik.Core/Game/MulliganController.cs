using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// Runs the London mulligan loop (CR 103.4) for a single player:
/// draw 7 → ask agent keep-or-mull → on mull, put the hand back into
/// the library, SHUFFLE THE LIBRARY (CR 103.4 — "shuffles their hand
/// into their library"), then re-prompt (cap at 7 mulligans). When
/// the agent finally keeps, the player puts N cards on the bottom of
/// their library where N = mulligans taken.
///
/// The shuffle goes through <see cref="LibraryShuffle.ShuffleLibrary"/>
/// so it uses the per-player <see cref="GameRandom"/> registered in
/// <see cref="GameRandomRegistry"/> (deterministic replay) and
/// publishes a <see cref="LibraryShuffledEvent"/> to any subscribed
/// event bus — same shape as game-start + tutor shuffles.
/// </summary>
public sealed class MulliganController
{
    public async Task<int> RunAsync(
        Player player,
        IPlayerAgent agent,
        GameContext ctx,
        int handSize = 7,
        int maxMulligans = 7,
        CancellationToken ct = default)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (agent == null) throw new ArgumentNullException(nameof(agent));

        var mulligansTaken = 0;
        while (true)
        {
            // Draw 7 (always, per London).
            Draw(player, handSize);

            var hand = player.Zones.Hand.GetCards().ToList();
            var decision = await agent.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);

            if (decision == MulliganDecision.Keep || mulligansTaken >= maxMulligans)
            {
                if (mulligansTaken > 0)
                {
                    var chosen = await agent.ChooseCardsToBottomAsync(ctx, hand, mulligansTaken, ct);
                    foreach (var c in chosen)
                    {
                        player.Zones.Hand.RemoveCard(c);
                        player.Zones.Library.AddCard(c);
                        c.SetZone(ZoneType.Library);
                    }
                }
                return mulligansTaken;
            }

            // CR 103.4 — "the player shuffles their hand into their
            // library". Move the 7 in hand back to the library, then
            // shuffle the WHOLE library (not just the returned cards).
            // Pre-fix this step skipped the shuffle, so the next iteration
            // just bubbled the cards previously at positions 7..13 to
            // the top of the library and drew an entirely predictable
            // (i.e. non-mulligan) hand. CR 103.4 explicitly requires a
            // shuffle.
            foreach (var card in hand)
            {
                player.Zones.Hand.RemoveCard(card);
                player.Zones.Library.AddCard(card);
                card.SetZone(ZoneType.Library);
            }
            LibraryShuffle.ShuffleLibrary(player, "mulligan");
            mulligansTaken++;
        }
    }

    private static void Draw(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }

    private static void BottomCards(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var top = player.Zones.Hand.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Hand.RemoveCard(top);
            player.Zones.Library.AddCard(top);
            top.SetZone(ZoneType.Library);
        }
    }
}
