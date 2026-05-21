using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Scry/surveil ordering heuristic. Keeps lands on top when mana-light and
/// pushes them to the bottom (or graveyard) when mana-flooded; mirrors the
/// inverse for spells.
/// </summary>
public static class ScrySurveilPolicy
{
    private const int FloodedThreshold = 5;
    private const int ScrewedThreshold = 2;

    public static ScryAction.ScryDecision Scry(
        GameContext ctx, Player self, IReadOnlyList<ICard> peeked)
    {
        var lands = self.Zones.Battlefield.GetCards().OfType<Land>().Count();
        var toBottom = new List<ICard>();
        var topOrder = new List<ICard>();
        foreach (var card in peeked)
        {
            var isLand = card is Land;
            if (isLand && lands >= FloodedThreshold) toBottom.Add(card);
            else if (!isLand && lands < ScrewedThreshold) toBottom.Add(card);
            else topOrder.Add(card);
        }
        return new ScryAction.ScryDecision(toBottom, topOrder);
    }

    public static SurveilAction.SurveilDecision Surveil(
        GameContext ctx, Player self, IReadOnlyList<ICard> peeked)
    {
        var lands = self.Zones.Battlefield.GetCards().OfType<Land>().Count();
        var toGraveyard = new List<ICard>();
        var topOrder = new List<ICard>();
        foreach (var card in peeked)
        {
            if (card is Land && lands >= FloodedThreshold) toGraveyard.Add(card);
            else topOrder.Add(card);
        }
        return new SurveilAction.SurveilDecision(toGraveyard, topOrder);
    }
}
