using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// Runs the London mulligan loop (Rule 103.4) for a single player:
/// draw 7 → ask agent keep-or-mull → on mull, shuffle hand back and repeat
/// (cap at 7 mulligans). When the agent finally keeps, the player puts N
/// cards on the bottom of their library where N = mulligans taken.
///
/// First-pass simplification: "bottom" picks the first N cards in hand
/// (Phase 10.5 can add a `ChooseCardsToBottomAsync` prompt). Library is
/// not actually shuffled either — that's a deck-builder concern.
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
                BottomCards(player, mulligansTaken);
                return mulligansTaken;
            }

            // Put hand back in library (no real shuffle yet — Phase 10.5).
            foreach (var card in hand)
            {
                player.Zones.Hand.RemoveCard(card);
                player.Zones.Library.AddCard(card);
                card.Zone = ZoneType.Library;
            }
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
            top.Zone = ZoneType.Hand;
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
            top.Zone = ZoneType.Library;
        }
    }
}
