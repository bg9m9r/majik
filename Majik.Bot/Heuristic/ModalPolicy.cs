using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Picks modes (CR 700.2) and X-cost values (CR 107.3). v1 heuristics:
/// always pick the first mode; X = total lands on the battlefield.
/// </summary>
public static class ModalPolicy
{
    public static int PickMode(GameContext ctx, Player self, IReadOnlyList<string> modes) => 0;

    public static int PickX(GameContext ctx, Player self)
        => self.Zones.Battlefield.GetCards().OfType<Land>().Count();
}
