using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Lifecycle binder for Archon of Emeria's printed static replacement
/// (CR 614.1c):
///   "Nonbasic lands your opponents control enter tapped."
///
/// A strict subset of <see cref="ThaliaHereticCatharEntersTappedEffect"/>
/// ("Creatures AND nonbasic lands…"): Archon taps only nonbasic LANDS (no
/// creatures). While the source permanent (Archon) is on the battlefield, an
/// <see cref="IReplacementEffect{ZoneMoveIntent}"/> is registered on the
/// supplied <see cref="ReplacementBus"/> that rewrites any battlefield-entry
/// intent carrying a non-basic <see cref="CardType.Land"/> whose controller is
/// an opponent of Archon's controller, setting
/// <see cref="ZoneMoveIntent.EntersTapped"/> = true.
///
/// ## Land filter (CR 305.6)
/// A land is basic iff it carries the <see cref="CardSupertype.Basic"/>
/// supertype; anything else with the Land card type is a nonbasic land. Only
/// nonbasic lands are tapped — basic lands enter untapped.
///
/// ## Symmetry / scope (CR 109.5)
/// One-sided: only lands whose controller is an opponent of Archon's
/// controller are tapped. The opponent test uses <see cref="Player"/> reference
/// inequality against the source's current controller, recomputed per intent so
/// a control-change of Archon flips which side is taxed.
///
/// ## Lifecycle (ETB / LTB)
/// Mirrors <see cref="ThaliaHereticCatharEntersTappedEffect"/>: subscribe to
/// <see cref="CardMovedEvent"/> + register on Attach, re-sync on every zone move
/// of the source, unregister when Archon leaves the battlefield.
/// </summary>
public sealed class ArchonOfEmeriaLandsEnterTappedEffect
{
    private readonly Permanent _source;
    private readonly ReplacementBus _bus;
    private readonly IEventBus? _eventBus;
    private readonly Action<CardMovedEvent> _handler;
    private readonly LambdaReplacement<ZoneMoveIntent> _effect;
    private bool _attached;
    private bool _registered;

    /// <param name="source">The Archon permanent gating the effect. Must be
    /// non-null; the replacement only applies while the source is on the
    /// battlefield.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to register
    /// on. Must be non-null.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>. May be
    /// null — Attach will still sync once.</param>
    public ArchonOfEmeriaLandsEnterTappedEffect(
        Permanent source,
        ReplacementBus replacementBus,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bus = replacementBus ?? throw new ArgumentNullException(nameof(replacementBus));
        _eventBus = eventBus;
        _handler = OnEvent;

        _effect = new LambdaReplacement<ZoneMoveIntent>(
            applies: (intent, _) =>
                intent.ToZone == ZoneType.Battlefield
                && intent.FromZone != ZoneType.Battlefield
                && IsNonbasicLand(intent.Card)
                && ControlledByOpponentOfSource(intent),
            replace: static (intent, _) =>
                intent.EntersTapped ? intent : intent with { EntersTapped = true },
            oneShot: false,
            tag: this);
    }

    /// <summary>Whether the replacement is currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>
    /// Subscribe to zone-move events and register if the source is already on
    /// the battlefield. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        Sync();
    }

    /// <summary>Unsubscribe and remove the registration. Idempotent.</summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        Unregister();
    }

    // CR 305.6 — a land is basic iff it has the Basic supertype. Anything else
    // with the Land card type is a nonbasic land. (No creature clause — Archon
    // only taps lands, unlike Thalia, Heretic Cathar.)
    private static bool IsNonbasicLand(ICard card)
        => card.HasType(CardType.Land) && !card.HasSupertype(CardSupertype.Basic);

    // CR 109.5 / CR 102.2 — "your opponents control": the entering land's
    // controller must be a player other than Archon's controller.
    private bool ControlledByOpponentOfSource(ZoneMoveIntent intent)
    {
        var sourceController = _source.Controller ?? _source.Owner;
        if (sourceController is null) return false;

        var enteringController =
            intent.Controller ?? intent.Card.Controller ?? intent.Card.Owner;
        if (enteringController is null) return false;

        return !ReferenceEquals(enteringController, sourceController);
    }

    private void OnEvent(CardMovedEvent e)
    {
        if (!ReferenceEquals(e.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (_source.Zone == ZoneType.Battlefield)
        {
            if (_registered) return;
            _bus.Register(_effect);
            _registered = true;
        }
        else
        {
            Unregister();
        }
    }

    private void Unregister()
    {
        if (!_registered) return;
        _bus.Unregister(_effect);
        _registered = false;
    }
}
