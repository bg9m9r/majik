using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Bot.Tests.FullGameGate;

/// <summary>
/// CR 400.1 — every card is in exactly one zone. Shared by the full-game gate
/// and the bot-vs-bot fuzz observer (Majik.Bot.Tests.Integration).
/// </summary>
public static class ZoneIntegrity
{
    public static readonly IReadOnlyList<ZoneType> AllZones = new[]
    {
        ZoneType.Battlefield, ZoneType.Graveyard, ZoneType.Hand,
        ZoneType.Exile, ZoneType.Library, ZoneType.Stack, ZoneType.Command,
    };

    /// <summary>A card found in more than one zone (across every player's zones).</summary>
    public sealed record Duplicate(ICard Card, ZoneType FirstZone, ZoneType SecondZone)
    {
        /// <summary>"ZoneIntegrity|card|ZoneA+ZoneB", the two zones ordered by
        /// name. The pair separates different desyncs of one card (left on the
        /// Stack after resolving to the Battlefield vs to the Graveyard), and
        /// ordering it keeps the signature independent of the scan order.</summary>
        public string Signature
        {
            get
            {
                var zones = new[] { FirstZone.ToString(), SecondZone.ToString() };
                Array.Sort(zones, StringComparer.Ordinal);
                return $"ZoneIntegrity|{Card.Name}|{zones[0]}+{zones[1]}";
            }
        }
    }

    public static IReadOnlyList<Duplicate> FindDuplicates(IEnumerable<Player> players)
    {
        List<Duplicate>? found = null;
        var seen = new Dictionary<Guid, ZoneType>();
        foreach (var p in players)
        {
            foreach (var zt in AllZones)
            {
                var zone = p.Zones.GetZone(zt);
                if (zone is null) continue;
                foreach (var card in zone.GetCards())
                {
                    if (seen.TryGetValue(card.InstanceId, out var first))
                        (found ??= new List<Duplicate>()).Add(new Duplicate(card, first, zt));
                    else
                        seen[card.InstanceId] = zt;
                }
            }
        }
        return (IReadOnlyList<Duplicate>?)found ?? Array.Empty<Duplicate>();
    }
}
