using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Grumgully, the Generous's printed static
/// replacement effect (CR 614.1d):
///   "Each other non-Human creature you control enters with an additional
///    +1/+1 counter on it."
///
/// While the source permanent (Grumgully) is on the battlefield, an
/// <see cref="IReplacementEffect{ZoneMoveIntent}"/> is registered on the
/// supplied <see cref="ReplacementBus"/>. The effect intercepts any
/// <see cref="ZoneMoveIntent"/> that:
/// <list type="number">
///   <item>Has destination <see cref="ZoneType.Battlefield"/> (and is not
///         already on the battlefield).</item>
///   <item>Carries a <see cref="CardType.Creature"/>.</item>
///   <item>Carries a creature that is NOT a <see cref="CardSubtype.Human"/>
///         (CR 109.5 / the printed "non-Human" filter).</item>
///   <item>Will be controlled by Grumgully's controller (CR 109.5 —
///         "creature you control"). The entering permanent's controller is
///         read from <see cref="ZoneMoveIntent.Controller"/> when set, else
///         from the card's own controller/owner.</item>
///   <item>Is a DIFFERENT permanent than Grumgully itself — the printed
///         text says "Each OTHER … creature you control" (CR 109.5).
///         Grumgully does not give itself a counter on entry. (Grumgully is
///         itself a Goblin Shaman, i.e. already non-Human, so the explicit
///         "other" exclusion matters.)</item>
/// </list>
/// When all conditions hold the intent is rewritten with
/// <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> incremented by one so
/// <see cref="Majik.Core.Services.ZoneService"/> places the additional
/// +1/+1 counter after the creature lands. The increment is additive so two
/// Grumgullys (or a Grumgully plus a Metallic Mimic) stack their counters
/// (CR 616.1).
///
/// Lifecycle mirrors <see cref="MetallicMimicEntersWithCounterEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> and register on
///         Attach.</item>
///   <item>Re-sync on every zone move of the source.</item>
///   <item>Detach / leaving the battlefield unregisters from the bus.</item>
/// </list>
/// </summary>
public sealed class GrumgullyEntersWithCounterEffect
{
    private readonly Permanent _source;
    private readonly ReplacementBus _bus;
    private readonly IEventBus? _eventBus;
    private readonly Action<CardMovedEvent> _handler;
    private readonly LambdaReplacement<ZoneMoveIntent> _effect;
    private bool _attached;
    private bool _registered;

    /// <param name="source">The Grumgully permanent gating the effect. Must be
    /// non-null; the replacement only applies while the source is on the
    /// battlefield.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register on. Must be non-null.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public GrumgullyEntersWithCounterEffect(
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

        // "Each OTHER … creature" (CR 109.5) — Grumgully never counters
        // itself.
        if (ReferenceEquals(intent.Card, _source)) return false;

        // Only creatures qualify.
        if (!intent.Card.HasType(CardType.Creature)) return false;

        // "non-Human" — a creature with the Human subtype is excluded.
        if (intent.Card.HasSubtype(CardSubtype.Human)) return false;

        // "creature YOU control" (CR 109.5) — the entering permanent's
        // controller must match Grumgully's current controller.
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
