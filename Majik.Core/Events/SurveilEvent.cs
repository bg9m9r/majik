using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// CR 701.42 — Event fired when a player surveils. Published by
/// <see cref="Majik.Core.Primitives.Fx.Surveil"/> after the surveil
/// action resolves (peeked cards are partitioned into graveyard-bound
/// and library-top-bound by the agent decision, library + graveyard
/// state is updated), so subscribers observe the post-resolve state.
///
/// <para>
/// This is the surface "Whenever you surveil" / "Whenever ~ surveils"
/// triggers subscribe to (Ledger Shredder, Glimpse the Unthinkable
/// family, Dimir Spybug, etc.). The triggering player is the player
/// who surveiled; the cards carried are the cards that were peeked
/// (regardless of where they ended up post-decision) — this matches
/// the wording of "look at the top N cards" so card-specific predicates
/// can inspect what was seen.
/// </para>
///
/// <para>
/// CR 701.42a — "To surveil N, look at the top N cards of your library,
/// then put any number of them into your graveyard and the rest on top
/// of your library in any order." Published once per surveil
/// resolution; if multiple surveil effects resolve sequentially each
/// publishes its own event.
/// </para>
/// </summary>
public class SurveilEvent : GameEvent
{
    /// <summary>The player who surveiled.</summary>
    public Player Player { get; }

    /// <summary>How many cards were surveilled (the N in "surveil N").
    /// May exceed <see cref="Cards"/>'s count if the library had fewer
    /// than N cards — the count reflects the requested value.</summary>
    public int N { get; }

    /// <summary>The cards that were peeked / surveilled (in pre-decision
    /// library order, top-first). Some end up in the graveyard, some on
    /// top of the library — subscribers inspect the cards' current zone
    /// to tell which is which. May be empty if the library was empty.</summary>
    public IReadOnlyList<ICard> Cards { get; }

    public SurveilEvent(Player player, int n, IReadOnlyList<ICard> cards)
        : base(EventType.Surveil)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        N = n;
        Cards = cards ?? throw new ArgumentNullException(nameof(cards));
    }
}
