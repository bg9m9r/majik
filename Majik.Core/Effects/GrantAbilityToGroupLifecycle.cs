using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

// (TriggerManager lives in Majik.Core.Abilities — already imported above.)

/// <summary>
/// Reusable lifecycle binder for a CR 613.1f Layer-6 group ability-grant
/// (<see cref="GrantAbilityToGroupStaticEffect"/>) — the battlefield-gated
/// register / revoke seam used across the static-effect family
/// (<see cref="RetypeLandsStaticEffect"/>,
/// <see cref="GrantLandSubtypeStaticEffect"/>): subscribe to
/// <see cref="CardMovedEvent"/>, register the effect when the source enters
/// the battlefield, unregister (revoking every bearer) when it leaves.
///
/// Concrete example: Chromatic Lantern — "Lands you control have '{T}: Add
/// one mana of any color.'" — scope <c>p =&gt; p is Land &amp;&amp;
/// ReferenceEquals(p.Controller, source.Controller)</c>, abilityFactory =
/// five single-colour <see cref="ManaAbility"/> instances on each member.
/// Other group-grant cards reuse this with a different scope /
/// abilityFactory ("creatures you control have trample", Cryptolith
/// Rite-style mana grants, …).
/// </summary>
public sealed class GrantAbilityToGroupLifecycle
{
    private readonly Permanent _source;
    private readonly ContinuousEffectsService _effects;
    private readonly IEventBus? _eventBus;
    private readonly Func<Permanent, bool> _scope;
    private readonly Func<Permanent, IReadOnlyList<IAbility>> _abilityFactory;
    private readonly Func<IEnumerable<Permanent>> _membershipProvider;
    private readonly TriggerManager? _triggers;
    private readonly Action? _onLeaveBattlefield;
    private readonly Action<CardMovedEvent> _handler;
    private GrantAbilityToGroupStaticEffect? _registered;
    private bool _attached;

    /// <param name="source">The permanent whose presence on the battlefield
    /// gates the effect.</param>
    /// <param name="layers">The continuous-effects service the group grant
    /// registers against.</param>
    /// <param name="eventBus">Bus to subscribe for
    /// <see cref="CardMovedEvent"/>. May be null — the grant still activates
    /// on <see cref="Attach"/> if the source is already on the battlefield,
    /// but no zone-change tracking happens.</param>
    /// <param name="scope">Controller-scoped membership filter.</param>
    /// <param name="abilityFactory">Builds a fresh batch of abilities per
    /// member.</param>
    /// <param name="membershipProvider">Returns the live candidate set
    /// (typically the source controller's battlefield).</param>
    /// <param name="triggers">Optional live <see cref="TriggerManager"/>,
    /// threaded into the registered
    /// <see cref="GrantAbilityToGroupStaticEffect"/> so a TRIGGERED ability
    /// granted to the group is registered / unregistered with the manager as
    /// membership changes (e.g. Kataki's per-artifact upkeep tax). Null for the
    /// activated / mana group-grant family (Chromatic Lantern, Cryptolith
    /// Rite), where the granted abilities surface purely through the bearer's
    /// <see cref="Card.Abilities"/> list.</param>
    /// <param name="onLeaveBattlefield">Optional teardown callback invoked
    /// exactly when the source LEAVES the battlefield (the registered grant
    /// transitions from active → revoked). Fires once per leave, after the
    /// grant is unregistered, and NOT for a source that was never on the
    /// battlefield. Used by Agatha's Soul Cauldron to detach its imprint
    /// back-links (CR 702.49): the imprinted cards stay in exile but lose their
    /// link to the (now-gone) Cauldron — they do NOT return.</param>
    public GrantAbilityToGroupLifecycle(
        Permanent source,
        ContinuousEffectsService layers,
        IEventBus? eventBus,
        Func<Permanent, bool> scope,
        Func<Permanent, IReadOnlyList<IAbility>> abilityFactory,
        Func<IEnumerable<Permanent>> membershipProvider,
        TriggerManager? triggers = null,
        Action? onLeaveBattlefield = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _effects = layers ?? throw new ArgumentNullException(nameof(layers));
        _eventBus = eventBus;
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _abilityFactory = abilityFactory ?? throw new ArgumentNullException(nameof(abilityFactory));
        _membershipProvider = membershipProvider ?? throw new ArgumentNullException(nameof(membershipProvider));
        _triggers = triggers;
        _onLeaveBattlefield = onLeaveBattlefield;
        _handler = OnEvent;
    }

    /// <summary>Whether the Layer-6 group grant is currently registered.</summary>
    public bool IsActive => _registered != null;

    /// <summary>
    /// Subscribe to zone-move events and register the group grant if the
    /// source is already on the battlefield. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        Sync();
    }

    /// <summary>
    /// Force a full re-grant of the registered group static (revoke every
    /// bearer, then re-run the ability factory for all current members). Use
    /// when the abilities the factory would PRODUCE have changed independently
    /// of membership — e.g. Agatha's Soul Cauldron imprinting a new creature
    /// card. No-op when the grant is not currently registered.
    /// </summary>
    public void Refresh() => _registered?.Refresh();

    /// <summary>Unsubscribe and unregister (revoking every bearer). Idempotent.</summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        Unregister();
    }

    private void OnEvent(CardMovedEvent e)
    {
        // React to the SOURCE entering/leaving (register/unregister), and to
        // ANY battlefield move (a group member entering/leaving) by re-syncing
        // the live membership while registered (CR 611.2c).
        if (ReferenceEquals(e.Card, _source))
        {
            Sync();
            return;
        }
        _registered?.Sync();
    }

    private void Sync()
    {
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;
        if (shouldBeActive && _registered == null)
        {
            _registered = new GrantAbilityToGroupStaticEffect(
                _source,
                _scope,
                _abilityFactory,
                _membershipProvider,
                _triggers);
            _effects.Register(_registered);
            _registered.Sync();
        }
        else if (!shouldBeActive && _registered != null)
        {
            // The source just LEFT the battlefield (was active, now isn't).
            // Revoke every bearer, then run the leave-the-battlefield teardown
            // (e.g. Agatha's imprint-link detach). The order matters only in
            // that the grant is gone before any client re-snapshots.
            Unregister();
            _onLeaveBattlefield?.Invoke();
        }
    }

    private void Unregister()
    {
        if (_registered == null) return;
        _effects.Unregister(_registered);
        _registered = null;
    }
}
