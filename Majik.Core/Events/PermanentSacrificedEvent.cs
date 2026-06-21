using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// CR 701.16 — event fired the moment a permanent is <b>sacrificed</b>
/// (an additional cost, a sacrifice effect, Annihilator, or an edict), i.e.
/// when that permanent leaves the battlefield <em>as a sacrifice</em> for
/// its owner's graveyard. Published by the bus-aware
/// <see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard, Player, IEventBus)"/>
/// overload AFTER the zone move completes (the card is already in the
/// graveyard when subscribers observe the event), so a payoff that pulls
/// the card back (It That Betrays — "put that card onto the battlefield
/// under your control") reads it from the graveyard.
///
/// <para>
/// This is the dedicated sacrifice-detection surface that "Whenever a/an
/// [player/opponent] sacrifices a [permanent]" triggers subscribe to.
/// Unlike a raw <see cref="CardMovedEvent"/> Battlefield → Graveyard
/// condition — which can't distinguish a sacrifice from a destroy / SBA
/// death and which <see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard)"/>
/// (the legacy overload) never even publishes — this event fires ONLY on a
/// real sacrifice and carries enough context to filter correctly:
/// <list type="bullet">
///   <item><see cref="SacrificingPlayer"/> — the player who sacrificed the
///     permanent (CR 701.16a — "its controller"), so a trigger can scope
///     to "an opponent sacrifices …" (It That Betrays) vs "you sacrifice …"
///     (Writhing Chrysalis).</item>
///   <item><see cref="WasToken"/> — whether the sacrificed permanent was a
///     token, so "sacrifices a <b>nontoken</b> permanent" (It That Betrays)
///     can skip tokens (a token in the graveyard ceases to exist as an SBA
///     per CR 111.7 — there is nothing to steal).</item>
/// </list>
/// </para>
/// </summary>
public class PermanentSacrificedEvent : GameEvent
{
    /// <summary>The permanent that was sacrificed. By the time the event is
    /// published it is already in its owner's graveyard (CR 701.16a — the
    /// permanent is put into its owner's graveyard); inspect
    /// <see cref="ICard.Zone"/> for its current zone.</summary>
    public ICard SacrificedCard { get; }

    /// <summary>The player who sacrificed the permanent (CR 701.16a — the
    /// permanent's controller at the time of the sacrifice). The
    /// triggering player a "whenever an opponent sacrifices …" /
    /// "whenever you sacrifice …" clause scopes against (CR 109.5).</summary>
    public Player SacrificingPlayer { get; }

    /// <summary>Whether the sacrificed permanent was a token (CR 111.7). A
    /// "nontoken permanent" clause (It That Betrays) skips the event when
    /// this is <see langword="true"/>.</summary>
    public bool WasToken { get; }

    public PermanentSacrificedEvent(ICard sacrificedCard, Player sacrificingPlayer, bool wasToken)
        : base()
    {
        SacrificedCard = sacrificedCard ?? throw new ArgumentNullException(nameof(sacrificedCard));
        SacrificingPlayer = sacrificingPlayer ?? throw new ArgumentNullException(nameof(sacrificingPlayer));
        WasToken = wasToken;
    }
}
