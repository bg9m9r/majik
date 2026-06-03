using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// CR 701.21 — fires the moment a permanent becomes tapped, at <em>every</em>
/// tap site (a tap cost, a "tap target …" effect, the attack tap CR 508.1f,
/// or a manual <see cref="Permanent.Tap()"/>). Published by
/// <see cref="Permanent.Tap(Player?)"/> via the ambient
/// <see cref="EventBusRegistry"/> — exactly once per real tap (the method
/// throws if the permanent is already tapped, so it never double-fires).
///
/// <para>
/// <see cref="CausedBy"/> records the player who <em>tapped</em> the permanent
/// when that is known to the caller. This is what powers a
/// "<b>whenever you tap</b> a creature …" trigger (Solitary Sanctuary,
/// CR 603.2 — the trigger event is "you tapping", not the permanent becoming
/// tapped on its own). When a tap site can't attribute a tapper (e.g. a bare
/// engine <see cref="Permanent.Tap()"/> with no actor context) it is left
/// <see langword="null"/> and such triggers do not fire — they require a
/// known "you".
/// </para>
///
/// <para>
/// This is a domain event (not surfaced on the wire — no portal DTO), so it
/// does not touch the OpenAPI contract.
/// </para>
/// </summary>
public class PermanentTappedEvent : GameEvent
{
    /// <summary>The permanent that just became tapped.</summary>
    public Permanent Permanent { get; }

    /// <summary>
    /// The player who caused the tap (the "you" in "whenever you tap …"),
    /// or <see langword="null"/> when the tap site did not attribute an actor.
    /// </summary>
    public Player? CausedBy { get; }

    public PermanentTappedEvent(Permanent permanent, Player? causedBy = null)
        : base(EventType.Tapped)
    {
        Permanent = permanent ?? throw new ArgumentNullException(nameof(permanent));
        CausedBy = causedBy;
    }
}
