using Majik.Core.Cards;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Heuristic;

/// <summary>
/// London mulligan policy (CR 103.4). Counts lands; threshold loosens
/// with each mulligan taken.
/// </summary>
public static class MulliganPolicy
{
    public static MulliganDecision Decide(IReadOnlyList<ICard> hand, int mulligansTaken)
    {
        var lands = hand.Count(c => c is Land);
        var nonlands = hand.Count - lands;

        if (mulligansTaken >= 2) return MulliganDecision.Keep;

        if (lands >= 2 && lands <= 5 && nonlands >= 1)
            return MulliganDecision.Keep;

        return MulliganDecision.Mulligan;
    }
}
