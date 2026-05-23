using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 303.4 / 613.1f — lifecycle binder for an Aura that grants an
/// activated ability to its enchanted permanent. While the aura is on the
/// battlefield AND attached to a permanent, the activated ability built
/// by <see cref="_abilityFactory"/> sits on the bearer's
/// <see cref="Card.Abilities"/> collection. When the aura leaves the
/// battlefield or detaches, the granted ability is removed.
///
/// Family: Splinter Twin — "Enchanted creature has '{T}: Create a token
/// that's a copy of this creature, except it has haste. Exile the token
/// at the beginning of the next end step.'"
///
/// Lifecycle mirrors <see cref="AttachedAuraRetypeStaticEffect"/>:
/// subscribe to <see cref="CardMovedEvent"/>, sync on every move involving
/// the aura. The scope predicate reads <see cref="Permanent.AttachedTo"/>
/// at call time, so re-targeting (e.g. an aura that "moves" via control
/// change rituals) would re-bind correctly.
///
/// The granted ability is built on demand via <c>_abilityFactory(bearer)</c>
/// — this lets the factory wire the ability's <see cref="ActivatedAbility.Source"/>
/// and any closures (cost / effect) to the live bearer instance, which is
/// what the engine's activation flow + cost payment expect.
/// </summary>
public sealed class AttachedAuraAbilityGrantStaticEffect
{
    private readonly Permanent _aura;
    private readonly IEventBus? _eventBus;
    private readonly Func<Permanent, IAbility> _abilityFactory;
    private readonly Action<GameEvent> _handler;
    private Permanent? _grantedTo;
    private IAbility? _grantedAbility;
    private bool _attached;

    /// <summary>
    /// Build a lifecycle binder. The granted ability is produced by
    /// <paramref name="abilityFactory"/> on each (re-)grant; the factory
    /// receives the current bearer so closures can capture the live
    /// instance.
    /// </summary>
    public AttachedAuraAbilityGrantStaticEffect(
        Permanent auraSource,
        IEventBus? eventBus,
        Func<Permanent, IAbility> abilityFactory)
    {
        _aura = auraSource ?? throw new ArgumentNullException(nameof(auraSource));
        _eventBus = eventBus;
        _abilityFactory = abilityFactory ?? throw new ArgumentNullException(nameof(abilityFactory));
        _handler = OnEvent;
    }

    /// <summary>Whether the grant is currently registered on a bearer.</summary>
    public bool IsActive => _grantedAbility != null;

    /// <summary>The currently-granted ability, or null when no grant is live.</summary>
    public IAbility? GrantedAbility => _grantedAbility;

    /// <summary>The bearer the grant is currently registered on, or null.</summary>
    public Permanent? Bearer => _grantedTo;

    /// <summary>
    /// Subscribe to the bus and sync once. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.SubscribeAll(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and revoke the grant. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.UnsubscribeAll(_handler);
        Revoke();
    }

    /// <summary>
    /// Public hook to re-sync the grant — useful for callers that change
    /// <see cref="Permanent.AttachedTo"/> outside the
    /// <see cref="CardMovedEvent"/> path (test setup, control-change
    /// effects).
    /// </summary>
    public void Sync()
    {
        var onBattlefield = _aura.Zone == ZoneType.Battlefield;
        var desiredBearer = onBattlefield ? _aura.AttachedTo : null;

        if (ReferenceEquals(desiredBearer, _grantedTo))
        {
            return; // already matches
        }

        // Bearer changed (or grant should drop) — revoke the existing one,
        // then grant fresh on the new bearer if applicable.
        Revoke();

        if (desiredBearer != null)
        {
            _grantedAbility = _abilityFactory(desiredBearer);
            desiredBearer.AddAbility(_grantedAbility);
            _grantedTo = desiredBearer;
        }
    }

    private void OnEvent(GameEvent e)
    {
        if (e is not CardMovedEvent moved) return;
        // We care when the aura itself moves (LTB → revoke); the bearer
        // moving is handled by AttachmentLegalityCheck nulling AttachedTo
        // before the bearer's LTB publishes (CR 704.5n) — that change is
        // detected here via the Sync read of _aura.AttachedTo on the aura's
        // own move event chain. For now, conservatively sync on every
        // aura-related move.
        if (!ReferenceEquals(moved.Card, _aura)) return;
        Sync();
    }

    private void Revoke()
    {
        if (_grantedAbility == null || _grantedTo == null) return;
        _grantedTo.RemoveAbility(_grantedAbility);
        _grantedAbility = null;
        _grantedTo = null;
    }
}
