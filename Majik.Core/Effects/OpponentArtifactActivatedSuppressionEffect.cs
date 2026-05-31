using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Karn, the Great Creator — printed static (CR 602.5c / 605):
/// "Activated abilities of artifacts your opponents control can't be
/// activated."
///
/// Lifecycle binder analogous to <see cref="PithingNeedleStaticEffect"/>,
/// but instead of registering a single chosen card name into
/// <see cref="ActivatedAbilityRestrictions"/> it registers a
/// <b>predicate</b>: every candidate <see cref="IActivatedAbility"/> is
/// inspected, and activation is rejected when:
/// <list type="bullet">
///   <item>The ability's <c>Source</c> is an on-battlefield
///         <see cref="ICard"/> with <see cref="CardType.Artifact"/>, AND</item>
///   <item>That card's controller is NOT the suppressor's controller
///         (i.e. it is an opponent's permanent).</item>
/// </list>
///
/// CR 605 mana-ability exemption is enforced by
/// <see cref="ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(IActivatedAbility)"/>
/// before the predicate runs — Karn's static is consistent with that
/// since "activated abilities" in CR vocabulary excludes mana abilities
/// (CR 605.1a).
///
/// While the source permanent is on the battlefield, a predicate
/// restriction is registered into <see cref="ActivatedAbilityRestrictions"/>.
/// On Detach (or on LTB via <see cref="CardMovedEvent"/>), the
/// registration is removed. The controller used by the predicate is
/// captured at <see cref="Attach"/> time; if Karn changes controller
/// mid-game, the prior controller's "opponents" set is what gates the
/// suppression. Control-change re-evaluation is a follow-up.
/// </summary>
public sealed class OpponentArtifactActivatedSuppressionEffect
{
    private readonly Permanent? _source;
    private readonly IEventBus? _eventBus;
    private readonly Player _controller;
    private readonly object _token = new();
    private readonly Action<CardMovedEvent> _handler;
    private readonly Predicate<IActivatedAbility> _predicate;
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build the lifecycle binder.
    /// </summary>
    /// <param name="source">The permanent gating the effect. May be null
    /// for test scaffolding — pair with <paramref name="eventBus"/>
    /// = null and Attach() will sync once.</param>
    /// <param name="controller">The suppressor's controller. Opponents
    /// of this player have their artifact activated abilities blocked.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public OpponentArtifactActivatedSuppressionEffect(
        Permanent? source,
        Player controller,
        IEventBus? eventBus)
    {
        _source = source;
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _eventBus = eventBus;
        _handler = OnEvent;
        _predicate = IsBlocked;
    }

    /// <summary>Whether the suppression is currently registered.</summary>
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

    private bool ActiveGate()
    {
        if (_source == null) return true;
        return _source.Zone == ZoneType.Battlefield;
    }

    private void OnEvent(CardMovedEvent e)
    {
        var moved = e;
        if (_source != null && !ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (ActiveGate())
        {
            if (_registered) return;
            ActivatedAbilityRestrictions.AddPredicateRestriction(_token, _predicate);
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
        ActivatedAbilityRestrictions.RemovePredicateRestriction(_token);
        _registered = false;
    }

    /// <summary>
    /// Predicate body — return true if the activated ability's source is
    /// an artifact controlled by an opponent of <see cref="_controller"/>
    /// and currently on the battlefield (CR 602.2 — only on-battlefield
    /// permanents have their activated abilities gated by static effects;
    /// activated abilities from hand/graveyard/exile are outside this
    /// printed static's scope).
    /// </summary>
    private bool IsBlocked(IActivatedAbility ability)
    {
        // Defensive — the lifecycle binder is expected to Unregister this
        // predicate from the registry on LTB. In case a host fails to
        // clean up (e.g. xUnit parallel tests where one suite leaks state
        // before its Dispose fires), self-deactivate when our source is
        // no longer on the battlefield. This keeps the predicate honest
        // about the printed text's "while on the battlefield" implicit
        // scope (CR 604.2) without relying on prompt registry hygiene.
        if (_source != null && _source.Zone != ZoneType.Battlefield) return false;

        if (ability.Source is not ICard card) return false;
        if (!card.HasType(CardType.Artifact)) return false;
        // Only on-battlefield artifacts — Karn's printed static targets
        // artifact permanents, not "artifact cards" in any zone.
        if (card is Permanent perm && perm.Zone != ZoneType.Battlefield) return false;
        var src = card.Controller;
        if (src is null) return false;
        // "Your opponents" — anyone who is not the suppressor's controller.
        if (ReferenceEquals(src, _controller)) return false;
        return true;
    }
}
