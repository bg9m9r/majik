using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Bot.Evaluation;

/// <summary>
/// Ranks an opponent's permanents by how threatening they are. v1 ranks
/// by raw power desc, toughness desc as tiebreaker.
/// </summary>
public static class ThreatRanker
{
    public static IEnumerable<Creature> Rank(Player opponent)
        => opponent.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .OrderByDescending(c => c.Power)
            .ThenByDescending(c => c.Toughness);
}
