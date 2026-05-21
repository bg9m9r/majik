using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Picks targets for a TargetRequest. v1: from the legal candidate set,
/// prefer opponent's highest-power creatures. If no creatures, prefer
/// the opponent themselves; otherwise pick the first candidate.
/// </summary>
public static class TargetPolicy
{
    public static IReadOnlyList<object> Pick(
        GameContext ctx, Player self, TargetRequest request)
    {
        if (request.LegalCandidates.Count == 0)
            return Array.Empty<object>();

        var byPriority = request.LegalCandidates
            .OrderByDescending(c => Score(c, self))
            .ToList();

        var count = Math.Min(request.MaxTargets, byPriority.Count);
        return byPriority.Take(count).ToList();
    }

    private static double Score(object candidate, Player self)
    {
        if (candidate is Creature crt)
        {
            var ownedBySelf = ReferenceEquals(crt.Controller ?? crt.Owner, self);
            return ownedBySelf ? -100 : crt.Power * 10 + crt.Toughness;
        }
        if (candidate is Player p && !ReferenceEquals(p, self))
            return 5;
        return 0;
    }
}
