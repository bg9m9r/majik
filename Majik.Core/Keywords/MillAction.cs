using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.13 — Mill. To mill N cards, a player moves the top N cards of
/// their library into their graveyard. If the library has fewer than N
/// cards, the player mills all remaining cards; this alone doesn't cause
/// the player to lose the game (that comes from drawing from an empty
/// library at draw step).
/// </summary>
public static class MillAction
{
    /// <summary>
    /// Move up to <paramref name="count"/> cards from the top of
    /// <paramref name="player"/>'s library into their graveyard. Returns
    /// the cards actually milled in milled order (first milled first).
    /// <paramref name="count"/> &lt;= 0 is a no-op returning an empty list.
    /// </summary>
    public static IReadOnlyList<ICard> Apply(Player player, int count)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (count <= 0) return Array.Empty<ICard>();

        var library = player.Zones.Library;
        var graveyard = player.Zones.Graveyard;

        var toMill = library.GetCards().Take(count).ToList();
        foreach (var c in toMill)
        {
            library.RemoveCard(c);
            graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        return toMill;
    }
}
