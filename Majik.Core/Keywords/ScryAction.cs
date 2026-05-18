using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.20 / 702.130 — Scry N: look at top N cards of your library,
/// then put any number of them on the bottom of your library and the rest
/// back on top in any order. Requires a player agent decision in the
/// general case; this helper takes the decision pre-resolved as
/// <see cref="ScryDecision"/>.
/// </summary>
public static class ScryAction
{
    public sealed record ScryDecision(
        IReadOnlyList<ICard> ToBottom,
        IReadOnlyList<ICard> TopOrder);

    /// <summary>Reveal the top N cards (read-only peek for the agent).</summary>
    public static IReadOnlyList<ICard> Peek(Player player, int n) =>
        player.Zones.Library.GetCards().Take(n).ToList();

    /// <summary>
    /// Apply the agent's decision. <paramref name="decision"/> partitions the
    /// peeked top N into bottom-bound and top-bound cards; engine reorders
    /// the library accordingly.
    /// </summary>
    public static void Apply(Player player, int n, ScryDecision decision)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (decision == null) throw new ArgumentNullException(nameof(decision));

        var peeked = Peek(player, n);
        if (decision.ToBottom.Concat(decision.TopOrder).Count() != peeked.Count)
        {
            throw new InvalidOperationException("Scry decision must cover all peeked cards exactly once.");
        }

        // Remove all peeked cards from library.
        foreach (var c in peeked)
        {
            player.Zones.Library.RemoveCard(c);
        }

        // Re-insert top-bound first (order = caller's TopOrder; first = new top).
        // Library order: index 0 is the top. Insert in REVERSE so first listed ends on top.
        foreach (var c in decision.TopOrder.Reverse())
        {
            player.Zones.Library.AddCard(c);
            // AddCard appends; we need them at the front (top). Re-arrange.
        }

        // Naive: rebuild library top-first.
        var rest = player.Zones.Library.GetCards()
            .Where(c => !decision.TopOrder.Contains(c))
            .ToList();
        foreach (var c in player.Zones.Library.GetCards().ToList())
        {
            player.Zones.Library.RemoveCard(c);
        }
        foreach (var c in decision.TopOrder) player.Zones.Library.AddCard(c);
        foreach (var c in rest) player.Zones.Library.AddCard(c);
        foreach (var c in decision.ToBottom) player.Zones.Library.AddCard(c);
    }
}
