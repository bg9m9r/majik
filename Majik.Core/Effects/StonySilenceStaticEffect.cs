using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Stony Silence — printed static (CR 602.5c / 605):
/// "Activated abilities of artifacts can't be activated unless they're
/// mana abilities."
///
/// Lifecycle binder analogous to
/// <see cref="OpponentArtifactActivatedSuppressionEffect"/>, but global —
/// the predicate matches every artifact regardless of controller (both
/// players' artifacts are gated).
///
/// While the source permanent is on the battlefield, a predicate
/// restriction is registered into <see cref="ActivatedAbilityRestrictions"/>.
/// On Detach (or on LTB via <see cref="CardMovedEvent"/>), the
/// registration is removed.
///
/// CR 605 mana-ability exemption is enforced by
/// <see cref="ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(IActivatedAbility)"/>
/// before the predicate runs — Stony Silence's printed text is consistent
/// with that since "activated abilities" in CR vocabulary excludes mana
/// abilities (CR 605.1a). <see cref="Majik.Core.Services.ManaAbilityActivator"/>
/// further routes mana abilities around <see cref="ActionValidator"/>
/// entirely.
/// </summary>
public sealed class StonySilenceStaticEffect
{
    private readonly Permanent? _source;
    private readonly IEventBus? _eventBus;
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
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public StonySilenceStaticEffect(Permanent? source, IEventBus? eventBus)
    {
        _source = source;
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
    /// an artifact currently on the battlefield (CR 602.2 — only on-
    /// battlefield permanents have their activated abilities gated by
    /// printed static effects). Applies globally to BOTH controllers'
    /// artifacts; mana-ability exemption is applied by the registry
    /// itself before this predicate runs.
    /// </summary>
    private bool IsBlocked(IActivatedAbility ability)
    {
        // Defensive — mirror OpponentArtifactActivatedSuppressionEffect's
        // self-deactivation guard. If the lifecycle binder failed to
        // unregister on LTB (e.g. xUnit parallel-tests leaking state),
        // honour the printed "while on the battlefield" implicit scope
        // (CR 604.2) at consult time.
        if (_source != null && _source.Zone != ZoneType.Battlefield) return false;

        if (ability.Source is not ICard card) return false;
        if (!card.HasType(CardType.Artifact)) return false;
        // Only on-battlefield artifacts — Stony Silence's printed static
        // targets artifact permanents, not "artifact cards" in any zone.
        if (card is Permanent perm && perm.Zone != ZoneType.Battlefield) return false;
        return true;
    }
}
