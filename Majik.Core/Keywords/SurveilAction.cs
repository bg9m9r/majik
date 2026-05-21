using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.42 — Surveil N: look at the top N cards of your library, then
/// put any number of them into your graveyard and the rest back on top of
/// your library in any order. Requires a player agent decision in the
/// general case; this helper takes the decision pre-resolved as
/// <see cref="SurveilDecision"/>.
/// </summary>
public static class SurveilAction
{
    public sealed record SurveilDecision(
        IReadOnlyList<ICard> ToGraveyard,
        IReadOnlyList<ICard> TopOrder);

    /// <summary>Reveal the top N cards (read-only peek for the agent).</summary>
    public static IReadOnlyList<ICard> Peek(Player player, int n) =>
        player.Zones.Library.GetCards().Take(n).ToList();

    /// <summary>
    /// Apply the agent's decision. <paramref name="decision"/> must partition
    /// the peeked top N into graveyard-bound and top-bound cards exactly once
    /// each. Engine reorders the library accordingly: <c>TopOrder[0]</c> ends
    /// up as the new top. Graveyard-bound cards are appended to the graveyard
    /// in decision order.
    /// </summary>
    public static void Apply(Player player, int n, SurveilDecision decision)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (decision == null) throw new ArgumentNullException(nameof(decision));

        var peeked = Peek(player, n);
        if (decision.ToGraveyard.Concat(decision.TopOrder).Count() != peeked.Count)
        {
            throw new InvalidOperationException("Surveil decision must cover all peeked cards exactly once.");
        }

        // Remove all peeked cards from library.
        foreach (var c in peeked)
        {
            player.Zones.Library.RemoveCard(c);
        }

        // Rebuild library: TopOrder first, then remaining library cards (those
        // not touched by surveil), then nothing at the bottom — mirrors
        // ScryAction's "ToBottom" placement but sends those cards to graveyard
        // instead. Pattern is identical to ScryAction.Apply.
        var rest = player.Zones.Library.GetCards()
            .Where(c => !decision.TopOrder.Contains(c))
            .ToList();
        foreach (var c in player.Zones.Library.GetCards().ToList())
        {
            player.Zones.Library.RemoveCard(c);
        }
        foreach (var c in decision.TopOrder)
        {
            player.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
        foreach (var c in rest)
        {
            player.Zones.Library.AddCard(c);
        }

        // Send graveyard-bound cards to graveyard (in decision order).
        foreach (var c in decision.ToGraveyard)
        {
            player.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }
    }
}
