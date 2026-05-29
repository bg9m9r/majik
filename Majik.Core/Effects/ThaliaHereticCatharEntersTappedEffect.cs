using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Thalia, Heretic Cathar's printed static
/// replacement effect (CR 614.1c):
///   "Creatures and nonbasic lands your opponents control enter tapped."
///
/// While the source permanent (Thalia) is on the battlefield, an
/// <see cref="IReplacementEffect{ZoneMoveIntent}"/> is registered on the
/// supplied <see cref="ReplacementBus"/>. The effect intercepts any
/// <see cref="ZoneMoveIntent"/> that:
/// <list type="number">
///   <item>Has destination <see cref="ZoneType.Battlefield"/> (and is not
///         already on the battlefield).</item>
///   <item>Carries a card that is a <see cref="CardType.Creature"/> OR a
///         non-basic <see cref="CardType.Land"/> (CR 305.6 — basic lands
///         carry the <see cref="CardSupertype.Basic"/> supertype).</item>
///   <item>Will be controlled by one of Thalia's controller's opponents
///         (CR 109.5 / CR 102.2). The entering permanent's controller is
///         read from <see cref="ZoneMoveIntent.Controller"/> when set, else
///         from the card's own controller/owner.</item>
/// </list>
/// When all conditions hold the intent is rewritten with
/// <see cref="ZoneMoveIntent.EntersTapped"/> = true so
/// <see cref="Majik.Core.Services.ZoneService"/> taps the permanent on
/// landing.
///
/// ## Symmetry / scope (CR 109.5)
/// The replacement is one-sided: it only taps permanents whose controller
/// is an opponent of Thalia's controller. Thalia's controller's own
/// creatures and lands are untouched. The opponent test uses
/// <see cref="Player"/> reference inequality against the source's current
/// controller, recomputed on each intent so a control-change of Thalia
/// flips which side is taxed.
///
/// Lifecycle mirrors <see cref="ContainmentPriestExileReplacementEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> and register on
///         Attach.</item>
///   <item>Re-sync on every zone move of the source.</item>
///   <item>Detach / leaving the battlefield unregisters from the bus.</item>
/// </list>
/// </summary>
public sealed class ThaliaHereticCatharEntersTappedEffect
{
    private readonly Permanent _source;
    private readonly ReplacementBus _bus;
    private readonly IEventBus? _eventBus;
    private readonly Action<GameEvent> _handler;
    private readonly LambdaReplacement<ZoneMoveIntent> _effect;
    private bool _attached;
    private bool _registered;

    /// <param name="source">The Thalia permanent gating the effect. Must be
    /// non-null; the replacement only applies while the source is on the
    /// battlefield.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register on. Must be non-null.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public ThaliaHereticCatharEntersTappedEffect(
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
            applies: (intent, _) =>
                intent.ToZone == ZoneType.Battlefield
                && intent.FromZone != ZoneType.Battlefield
                && IsCreatureOrNonbasicLand(intent.Card)
                && ControlledByOpponentOfSource(intent),
            replace: static (intent, _) =>
                intent.EntersTapped ? intent : intent with { EntersTapped = true },
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
        _eventBus?.SubscribeAll(_handler);
        Sync();
    }

    /// <summary>Unsubscribe and remove the registration. Idempotent.</summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.UnsubscribeAll(_handler);
        Unregister();
    }

    // CR 305.6 — a land is basic iff it has the Basic supertype. Anything
    // else with the Land card type is a nonbasic land. Creatures are matched
    // unconditionally (the printed text says "Creatures and nonbasic lands").
    private static bool IsCreatureOrNonbasicLand(ICard card)
    {
        if (card.HasType(CardType.Creature)) return true;
        return card.HasType(CardType.Land) && !card.HasSupertype(CardSupertype.Basic);
    }

    // CR 109.5 / CR 102.2 — "your opponents control": the entering
    // permanent's controller must be a player other than Thalia's
    // controller. Resolve the entering controller from the intent (set when
    // ZoneService is told who will control the permanent) else from the
    // card's own controller/owner.
    private bool ControlledByOpponentOfSource(ZoneMoveIntent intent)
    {
        var sourceController = _source.Controller ?? _source.Owner;
        if (sourceController is null) return false;

        var enteringController =
            intent.Controller ?? intent.Card.Controller ?? intent.Card.Owner;
        if (enteringController is null) return false;

        return !ReferenceEquals(enteringController, sourceController);
    }

    private void OnEvent(GameEvent e)
    {
        if (e is not CardMovedEvent moved) return;
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
