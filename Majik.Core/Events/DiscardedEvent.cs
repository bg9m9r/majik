using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// CR 701.8 — event fired the moment a player <b>discards</b> a card (the
/// card moves from that player's hand to their graveyard as a discard).
/// Published AFTER the zone move completes (the card is already in the
/// graveyard when subscribers observe the event), mirroring the
/// <see cref="PermanentSacrificedEvent"/> / <see cref="SurveilEvent"/>
/// post-resolve posture.
///
/// <para>
/// This is the dedicated discard-detection surface that "Whenever you
/// discard a card, …" / "When you discard ~, …" triggers (Flameblade Adept,
/// Horror of the Broken Lands, Curator of Mysteries, the broader madness /
/// hellbent payoff family) subscribe to. Unlike a raw
/// <see cref="CardMovedEvent"/> Hand → Graveyard condition — which can't
/// distinguish a discard from any other hand→graveyard move (a sacrificed
/// card revealed from hand, a "put into graveyard" effect, …) — this event
/// fires ONLY on a real discard and carries enough context to filter
/// correctly:
/// <list type="bullet">
///   <item><see cref="Player"/> — the discarding player (CR 701.8a), so a
///     "Whenever <b>you</b> discard …" clause can scope to its own
///     controller (CR 109.5).</item>
///   <item><see cref="Card"/> — the discarded card (already in the
///     graveyard), so card-specific predicates ("discard a creature card",
///     "discard a land card") can inspect its printed types and so madness
///     can read the just-discarded card.</item>
///   <item><see cref="WasCost"/> — whether the discard was paid as a cost
///     (a discard cost / additional cost / "discard this card" activation)
///     versus performed by an effect or the cleanup-step max-hand-size
///     trim. A few triggers care about the distinction; most do not.</item>
/// </list>
/// </para>
///
/// <para>
/// Published by the central discard chokepoint
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> (which every real
/// discard route funnels through: discard effects via
/// <see cref="Majik.Core.Primitives.Fx.Discard"/>, the discard-cost surface
/// in <c>Majik.Core/Costs/</c>, and the cleanup-step trim in
/// <c>CleanupStep.DiscardToHandSize</c>). The bus is looked up best-effort
/// via <see cref="EventBusRegistry.Get(Player?)"/> — no publish if none is
/// registered (direct-construction unit tests).
/// </para>
/// </summary>
public class DiscardedEvent : GameEvent
{
    /// <summary>The player who discarded the card (CR 701.8a). The
    /// triggering player a "whenever you discard …" clause scopes against
    /// (CR 109.5).</summary>
    public Player Player { get; }

    /// <summary>The card that was discarded. By the time the event is
    /// published it is already in its owner's graveyard; inspect
    /// <see cref="ICard.Zone"/> for its current zone.</summary>
    public ICard Card { get; }

    /// <summary>Whether the discard was paid as a cost (discard cost,
    /// additional cost, or "discard this card" activation) rather than
    /// performed by an effect / the cleanup-step max-hand-size trim. Most
    /// triggers ignore this; it is carried for the few that distinguish
    /// (and for madness, which only applies to cost/effect discards, not
    /// the cleanup trim — CR 702.35).</summary>
    public bool WasCost { get; }

    public DiscardedEvent(Player player, ICard card, bool wasCost)
        : base()
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Card = card ?? throw new ArgumentNullException(nameof(card));
        WasCost = wasCost;
    }
}
