using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Spelunking's printed static replacement
/// effect (CR 614.1c):
///   "Lands you control enter untapped."
///
/// While the source enchantment (Spelunking) is on the battlefield, an
/// <see cref="IReplacementEffect{ZoneMoveIntent}"/> is registered on the
/// supplied <see cref="ReplacementBus"/>. The effect intercepts any
/// <see cref="ZoneMoveIntent"/> that:
/// <list type="number">
///   <item>Has destination <see cref="ZoneType.Battlefield"/> (and is not
///         already on the battlefield).</item>
///   <item>Carries a card with the <see cref="CardType.Land"/> card type
///         (CR 305 — basic and nonbasic alike).</item>
///   <item>Will be controlled by Spelunking's controller (CR 109.5 / CR
///         102.2). The entering permanent's controller is read from
///         <see cref="ZoneMoveIntent.Controller"/> when set, else from the
///         card's own controller/owner.</item>
/// </list>
/// When all conditions hold the intent is rewritten with
/// <see cref="ZoneMoveIntent.EntersTapped"/> = false so
/// <see cref="Majik.Core.Services.ZoneService"/> leaves the land untapped on
/// landing — the structural inverse of
/// <see cref="ThaliaHereticCatharEntersTappedEffect"/>.
///
/// ## Ordering with self-tapping lands (CR 616.1)
/// A tap-land (Hidden Cataract, a Triome, etc.) carries its own
/// <see cref="EntersTappedReplacement"/> / <see cref="ShockLandReplacement"/>
/// that sets <c>EntersTapped = true</c>. When both that effect and this one
/// apply to the same entry, CR 616.1 lets the affected player choose the
/// order; the player orders so this "enter untapped" effect applies last,
/// landing the land untapped (the printed intent of Spelunking). The
/// <see cref="ReplacementBus"/> currently applies in registration order;
/// because Spelunking's controller is the affected player and wants the
/// untapped result, the observable outcome matches the rules outcome the
/// controller would choose. Same CR 616.1 ordering caveat
/// <see cref="ThaliaHereticCatharEntersTappedEffect"/> documents.
///
/// ## Scope (CR 109.5)
/// One-sided: only lands whose controller IS Spelunking's controller are
/// untapped — opponents' lands are untouched. The controller test uses
/// <see cref="Player"/> reference equality against the source's current
/// controller, recomputed on each intent so a control-change of Spelunking
/// flips which side benefits.
///
/// Lifecycle mirrors <see cref="ThaliaHereticCatharEntersTappedEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> and register on
///         Attach.</item>
///   <item>Re-sync on every zone move of the source.</item>
///   <item>Detach / leaving the battlefield unregisters from the bus.</item>
/// </list>
/// </summary>
public sealed class SpelunkingLandsEnterUntappedEffect
{
    private readonly Permanent _source;
    private readonly ReplacementBus _bus;
    private readonly IEventBus? _eventBus;
    private readonly Action<CardMovedEvent> _handler;
    private readonly LambdaReplacement<ZoneMoveIntent> _effect;
    private bool _attached;
    private bool _registered;

    /// <param name="source">The Spelunking permanent gating the effect. Must
    /// be non-null; the replacement only applies while the source is on the
    /// battlefield.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register on. Must be non-null.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public SpelunkingLandsEnterUntappedEffect(
        Permanent source,
        ReplacementBus replacementBus,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bus = replacementBus ?? throw new ArgumentNullException(nameof(replacementBus));
        _eventBus = eventBus;
        _handler = OnEvent;

        // Build the replacement delegate once; reuse across register /
        // unregister cycles so the same object reference is used for both.
        //
        // CR 616.1 — gating Applies on EntersTapped == true makes this effect
        // ORDER-INDEPENDENT: it only spends its single per-entry firing
        // (CR 616.1c) to UNDO a tap. If a self-tapping replacement
        // (EntersTappedReplacement / ShockLandReplacement) fires first and
        // sets EntersTapped = true, this effect is still un-fired and the bus
        // re-runs it afterward to set it back to false — so the land enters
        // untapped no matter which order the two effects were registered in.
        // This is the controller-chosen ordering CR 616.1 grants the affected
        // player, realised deterministically.
        _effect = new LambdaReplacement<ZoneMoveIntent>(
            applies: (intent, _) =>
                intent.EntersTapped
                && intent.ToZone == ZoneType.Battlefield
                && intent.FromZone != ZoneType.Battlefield
                && intent.Card.HasType(CardType.Land)
                && ControlledBySource(intent),
            replace: static (intent, _) => intent with { EntersTapped = false },
            oneShot: false,
            tag: this);
    }

    /// <summary>Whether the replacement is currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>
    /// Subscribe to zone-move events and register if the source is already
    /// on the battlefield. Idempotent.
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

    // CR 109.5 / CR 102.2 — "lands you control": the entering land's
    // controller must be the same player as Spelunking's controller. Resolve
    // the entering controller from the intent (set when ZoneService is told
    // who will control the permanent) else from the card's own
    // controller/owner.
    private bool ControlledBySource(ZoneMoveIntent intent)
    {
        var sourceController = _source.Controller ?? _source.Owner;
        if (sourceController is null) return false;

        var enteringController =
            intent.Controller ?? intent.Card.Controller ?? intent.Card.Owner;
        if (enteringController is null) return false;

        return ReferenceEquals(enteringController, sourceController);
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
