using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Metallic Mimic's printed static replacement
/// effect (CR 614.1d):
///   "Each other creature you control of the chosen type enters with an
///    additional +1/+1 counter on it."
///
/// While the source permanent (Metallic Mimic) is on the battlefield, an
/// <see cref="IReplacementEffect{ZoneMoveIntent}"/> is registered on the
/// supplied <see cref="ReplacementBus"/>. The effect intercepts any
/// <see cref="ZoneMoveIntent"/> that:
/// <list type="number">
///   <item>Has destination <see cref="ZoneType.Battlefield"/> (and is not
///         already on the battlefield).</item>
///   <item>Carries a <see cref="CardType.Creature"/>.</item>
///   <item>Carries a creature of the chosen subtype (CR 614.1d — the
///         creature type chosen as Metallic Mimic entered).</item>
///   <item>Will be controlled by Metallic Mimic's controller (CR 109.5 —
///         "creature you control"). The entering permanent's controller is
///         read from <see cref="ZoneMoveIntent.Controller"/> when set, else
///         from the card's own controller/owner.</item>
///   <item>Is a DIFFERENT permanent than Metallic Mimic itself — the printed
///         text says "Each OTHER creature you control" (CR 109.5). Metallic
///         Mimic does not give itself a counter on entry.</item>
/// </list>
/// When all conditions hold the intent is rewritten with
/// <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> incremented by one so
/// <see cref="Majik.Core.Services.ZoneService"/> places the additional
/// +1/+1 counter after the creature lands. The increment is additive so two
/// Metallic Mimics naming the same type stack their counters (CR 616.1).
///
/// The chosen creature type is read lazily through
/// <see cref="_chosenType"/> on every intent, so a control change or a
/// not-yet-resolved choice is reflected correctly (the effect is a no-op
/// until a type is chosen).
///
/// Lifecycle mirrors <see cref="ThaliaHereticCatharEntersTappedEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> and register on
///         Attach.</item>
///   <item>Re-sync on every zone move of the source.</item>
///   <item>Detach / leaving the battlefield unregisters from the bus.</item>
/// </list>
/// </summary>
public sealed class MetallicMimicEntersWithCounterEffect
{
    private readonly Permanent _source;
    private readonly ReplacementBus _bus;
    private readonly IEventBus? _eventBus;
    private readonly Func<CardSubtype?> _chosenType;
    private readonly Action<CardMovedEvent> _handler;
    private readonly LambdaReplacement<ZoneMoveIntent> _effect;
    private bool _attached;
    private bool _registered;

    /// <param name="source">The Metallic Mimic permanent gating the effect.
    /// Must be non-null; the replacement only applies while the source is on
    /// the battlefield.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register on. Must be non-null.</param>
    /// <param name="chosenType">Resolves Metallic Mimic's chosen creature
    /// type. Read on every intent; returns null until a choice is made, in
    /// which case the replacement is a no-op. Must be non-null.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public MetallicMimicEntersWithCounterEffect(
        Permanent source,
        ReplacementBus replacementBus,
        Func<CardSubtype?> chosenType,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bus = replacementBus ?? throw new ArgumentNullException(nameof(replacementBus));
        _chosenType = chosenType ?? throw new ArgumentNullException(nameof(chosenType));
        _eventBus = eventBus;
        _handler = OnEvent;

        // Build the replacement delegate once; reuse across register /
        // unregister cycles so the same object reference is used for both.
        _effect = new LambdaReplacement<ZoneMoveIntent>(
            applies: (intent, _) => AppliesToEntry(intent),
            replace: static (intent, _) =>
                intent with { PlusOneCountersOnEnter = intent.PlusOneCountersOnEnter + 1 },
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

    private bool AppliesToEntry(ZoneMoveIntent intent)
    {
        if (intent.ToZone != ZoneType.Battlefield) return false;
        if (intent.FromZone == ZoneType.Battlefield) return false;

        // "Each OTHER creature" (CR 109.5) — Metallic Mimic never counters
        // itself.
        if (ReferenceEquals(intent.Card, _source)) return false;

        // Only creatures qualify.
        if (!intent.Card.HasType(CardType.Creature)) return false;

        // The choice must have been made; until then the effect is a no-op
        // (CR 614.1d — the chosen type scopes the replacement).
        var chosen = _chosenType();
        if (chosen is null) return false;
        if (!intent.Card.HasSubtype(chosen.Value)) return false;

        // "creature YOU control" (CR 109.5) — the entering permanent's
        // controller must match Metallic Mimic's current controller.
        var sourceController = _source.Controller ?? _source.Owner;
        if (sourceController is null) return false;

        var enteringController =
            intent.Controller ?? intent.Card.Controller ?? intent.Card.Owner;
        if (enteringController is null) return false;

        return ReferenceEquals(enteringController, sourceController);
    }

    private void OnEvent(CardMovedEvent e)
    {
        var moved = e;
        if (!ReferenceEquals(moved.Card, _source)) return;
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
