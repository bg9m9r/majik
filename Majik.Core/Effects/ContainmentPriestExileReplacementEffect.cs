using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Containment Priest's printed replacement
/// effect (CR 614):
///   "If a nontoken creature would enter the battlefield and it wasn't
///    cast, exile it instead."
///
/// While the source permanent is on the battlefield, an
/// <see cref="IReplacementEffect{ZoneMoveIntent}"/> is registered on the
/// supplied <see cref="ReplacementBus"/>. The effect intercepts any
/// <see cref="ZoneMoveIntent"/> that:
/// <list type="number">
///   <item>Has destination <see cref="ZoneType.Battlefield"/>.</item>
///   <item>Carries a card of type <see cref="CardType.Creature"/>.</item>
///   <item>Is not a token (<see cref="Permanent.IsToken"/> = false).</item>
///   <item>Has <see cref="ZoneMoveIntent.WasCast"/> = false.</item>
/// </list>
/// When all four conditions hold the destination is rewritten to
/// <see cref="ZoneType.Exile"/>.
///
/// ## Cast-marker sourcing (CR 113.5 / CR 400.7)
/// <see cref="ZoneMoveIntent.WasCast"/> is populated by
/// <see cref="Majik.Core.Services.ZoneService"/> from the persistent
/// <see cref="Majik.Core.Cards.Card.WasCast"/> flag stamped at cast
/// time by <see cref="Majik.Core.Game.SpellCastFlow"/>. Call sites that
/// put permanents onto the battlefield without going through
/// SpellCastFlow (Reanimate, Sneak Attack, Through the Breach, Aether
/// Vial, token creation, Show and Tell, blink reappearance) leave
/// <c>Card.WasCast</c> = false and therefore <c>intent.WasCast</c> =
/// false, so this replacement fires for them. ZoneService funnels every
/// zone move through its injected ReplacementBus (when one is supplied),
/// so enforcement is end-to-end once a shared bus is wired into both
/// ZoneService and the priest's factory.
///
/// Lifecycle mirrors <see cref="PithingNeedleStaticEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> and register on
///         Attach.</item>
///   <item>Sync on every relevant zone move.</item>
///   <item>Detach unregisters from the bus.</item>
/// </list>
/// </summary>
public sealed class ContainmentPriestExileReplacementEffect
{
    private readonly Permanent? _source;
    private readonly ReplacementBus _bus;
    private readonly IEventBus? _eventBus;
    private readonly Action<CardMovedEvent> _handler;
    private readonly LambdaReplacement<ZoneMoveIntent> _effect;
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build a Containment Priest exile-replacement lifecycle.
    /// </summary>
    /// <param name="source">The Containment Priest permanent gating the
    /// effect. Must be non-null; the effect only applies while the source
    /// is on the battlefield.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register on. Must be non-null.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public ContainmentPriestExileReplacementEffect(
        Permanent source,
        ReplacementBus replacementBus,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bus = replacementBus ?? throw new ArgumentNullException(nameof(replacementBus));
        _eventBus = eventBus;
        _handler = OnEvent;

        // Build the replacement effect delegate once; reuse across
        // register/unregister cycles so the same object reference is
        // used for both Register and Unregister.
        _effect = new LambdaReplacement<ZoneMoveIntent>(
            applies: static (intent, _) =>
                intent.ToZone == ZoneType.Battlefield
                && intent.Card.HasType(CardType.Creature)
                && (intent.Card is not Permanent p || !p.IsToken)
                && !intent.WasCast,
            replace: static (intent, _) =>
                intent with { ToZone = ZoneType.Exile },
            oneShot: false,
            tag: null);
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

    /// <summary>
    /// Unsubscribe and remove the registration. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        Unregister();
    }

    private void OnEvent(CardMovedEvent e)
    {
        var moved = e;
        if (!ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (_source?.Zone == ZoneType.Battlefield)
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
