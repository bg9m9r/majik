using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 718 — Plot. "{cost}: Plot this card from your hand. (Pay the plot cost
/// and exile it. You may cast it as a sorcery on a later turn without paying
/// its mana cost. Plot only as a sorcery.)"
///
/// <para>Reusable mechanic that tracks plotted cards (sibling of
/// <see cref="SuspendedCardRegistry"/>). Two halves:</para>
/// <list type="number">
///   <item><b>Plot the card</b> (<see cref="Plot"/>): a sorcery-speed special
///     action (CR 718.1) — pay the plot cost, exile the card from hand with a
///     "plotted" record, and stamp the turn number it was plotted.</item>
///   <item><b>Cast it later for free</b> (<see cref="CanCastPlotted"/> /
///     <see cref="MarkCastThisTurn"/>): on a LATER turn (CR 718.2 — not the
///     turn it was plotted), at sorcery speed, the controller may cast it
///     without paying its mana cost, at most once per turn per plotted card
///     (CR 718.2c).</item>
/// </list>
///
/// <para>The registry is UI-agnostic: it does not itself drive the cast
/// pipeline. The owning card / runtime calls <see cref="CanCastPlotted"/> to
/// gate the free cast and <see cref="MarkCastThisTurn"/> when the cast is
/// announced. The plot cost payment is delegated to the caller-supplied
/// <c>payPlotCost</c> so mana / sorcery-speed gating stays with the rules
/// engine.</para>
/// </summary>
public sealed class PlotRegistry
{
    private sealed class Entry
    {
        public ICard Card { get; init; } = null!;
        public Player Owner { get; init; } = null!;
        public int TurnPlotted { get; init; }
        public int LastTurnCast { get; set; } = -1;
    }

    private readonly List<Entry> _entries = new();

    /// <summary>
    /// CR 718.1 — plot <paramref name="card"/> from <paramref name="owner"/>'s
    /// hand on turn <paramref name="currentTurn"/>: pay the plot cost (via
    /// <paramref name="payPlotCost"/>) and exile the card with a plotted record.
    /// Returns false (no state change) when the card isn't in the owner's hand
    /// or the plot cost can't be paid. Plot is a sorcery-speed special action;
    /// the caller is responsible for the sorcery-speed timing gate (CR 718.1a).
    /// </summary>
    public bool Plot(ICard card, Player owner, int currentTurn, Func<bool> payPlotCost)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(payPlotCost);

        if (card.Zone != ZoneType.Hand) return false;
        if (!owner.Zones.Hand.ContainsCard(card)) return false;
        if (IsPlotted(card)) return false;

        if (!payPlotCost()) return false;

        // CR 718.1 — exile the card with a plot record.
        owner.Zones.Hand.RemoveCard(card);
        owner.Zones.Exile.AddCard(card);
        card.SetZone(ZoneType.Exile);

        _entries.Add(new Entry
        {
            Card = card,
            Owner = owner,
            TurnPlotted = currentTurn,
        });
        return true;
    }

    /// <summary>True if <paramref name="card"/> is currently plotted (exiled,
    /// waiting to be cast for free on a later turn).</summary>
    public bool IsPlotted(ICard card) =>
        _entries.Any(e => ReferenceEquals(e.Card, card));

    /// <summary>The turn number on which <paramref name="card"/> was plotted,
    /// or -1 if it isn't plotted.</summary>
    public int TurnPlotted(ICard card) =>
        _entries.FirstOrDefault(e => ReferenceEquals(e.Card, card))?.TurnPlotted ?? -1;

    /// <summary>
    /// CR 718.2 — may the plotted <paramref name="card"/> be cast for free on
    /// turn <paramref name="currentTurn"/>? True iff: the card is plotted, the
    /// current turn is strictly AFTER the turn it was plotted (CR 718.2 — "on a
    /// later turn", never the same turn), and it hasn't already been cast this
    /// turn (CR 718.2c — once per turn). Sorcery-speed timing is the caller's
    /// gate.
    /// </summary>
    public bool CanCastPlotted(ICard card, int currentTurn)
    {
        var e = _entries.FirstOrDefault(x => ReferenceEquals(x.Card, card));
        if (e == null) return false;
        if (currentTurn <= e.TurnPlotted) return false;      // CR 718.2 — later turn only
        if (e.LastTurnCast == currentTurn) return false;     // CR 718.2c — once per turn
        return true;
    }

    /// <summary>
    /// Record that the plotted <paramref name="card"/> was cast on turn
    /// <paramref name="currentTurn"/> (CR 718.2c — the once-per-turn cap). Call
    /// this when the free cast is announced. The card remains tracked until it
    /// leaves exile (<see cref="Remove"/>); a plotted card stays plottable for
    /// re-cast attempts only across distinct turns.
    /// </summary>
    public void MarkCastThisTurn(ICard card, int currentTurn)
    {
        var e = _entries.FirstOrDefault(x => ReferenceEquals(x.Card, card));
        if (e != null) e.LastTurnCast = currentTurn;
    }

    /// <summary>Stop tracking <paramref name="card"/> (e.g. once it actually
    /// leaves exile on resolution / is countered to graveyard).</summary>
    public void Remove(ICard card) =>
        _entries.RemoveAll(e => ReferenceEquals(e.Card, card));
}
