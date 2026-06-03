using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

// (TriggerManager lives in Majik.Core.Abilities — already imported above.)

/// <summary>
/// CR 613.1f — Layer 6 ability-adding continuous static that grants a
/// specified ability (activated / mana / triggered / keyword) to EVERY
/// permanent matching a controller-scoped filter, with LIVE membership
/// recomputed as permanents enter / leave the battlefield (CR 611.2c).
///
/// Generalises the single-target <see cref="GrantAbilityEffect"/> (one
/// dynamic <c>Func&lt;Permanent?&gt;</c> target) to a DYNAMIC GROUP. The
/// canonical case is Chromatic Lantern — "Lands you control have '{T}: Add
/// one mana of any color.'" — but the same shape covers any group-grant:
/// "creatures you control have trample / vigilance", Cryptolith Rite-style
/// mana grants, anthem-style ability grants, etc.
///
/// <para>Each granted ability is materialised on the bearer's
/// <see cref="Card.Abilities"/> list (via <see cref="Card.AddAbility"/>),
/// exactly as <see cref="GrantAbilityEffect"/> does for a single target.
/// MANA abilities therefore surface automatically through
/// <see cref="EffectiveManaAbilities"/> (which reads
/// <c>permanent.Abilities.OfType&lt;IManaAbility&gt;()</c>), so a land
/// granted "{T}: Add one mana of any color" can be tapped for any colour
/// (CR 605 / 616).</para>
///
/// <para>Lifecycle is keyed by the effect's <see cref="Source"/> permanent
/// and the <c>scope</c> filter, reconciled on every layer pass:
///   - <see cref="Sync"/> walks the live membership set
///     (<c>membershipProvider</c>): every battlefield permanent matching
///     <c>scope</c> that is not yet a bearer receives a fresh batch of
///     granted abilities; every former bearer that no longer matches (left
///     play, control changed, lost the relevant type) has its grant
///     revoked (CR 613.6e).
///   - When the source leaves play (<see cref="IsActive"/> false), every
///     grant is revoked.</para>
///
/// <para>The granted abilities are produced by <c>abilityFactory(member)</c>
/// per (re-)grant so closures bind to the live bearer. The effect does NOT
/// write to <see cref="PermanentCharacteristics"/> in the layer pass — like
/// <see cref="GrantAbilityEffect"/>, its <see cref="Apply"/> hooks are
/// no-ops; reconciliation is driven by
/// <see cref="ContinuousEffectsService"/> calling <see cref="Sync"/>.</para>
/// </summary>
public sealed class GrantAbilityToGroupStaticEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<Permanent, bool> _scope;
    private readonly Func<Permanent, IReadOnlyList<IAbility>> _abilityFactory;
    private readonly Func<IEnumerable<Permanent>> _membershipProvider;
    private readonly TriggerManager? _triggers;

    // Per-bearer granted abilities, so each can be revoked precisely when a
    // bearer drops out of the membership set. Reference-keyed: identity, not
    // value equality, distinguishes permanents.
    private readonly Dictionary<Permanent, IReadOnlyList<IAbility>> _granted
        = new(ReferenceEqualityComparer.Instance);

    /// <param name="source">CR 613.1g — the permanent generating the effect.
    /// The grant is alive only while <paramref name="source"/> is on the
    /// battlefield.</param>
    /// <param name="scope">Controller-scoped membership filter (e.g.
    /// <c>p =&gt; p is Land &amp;&amp; ReferenceEquals(p.Controller,
    /// source.Controller)</c> for "lands you control"). Evaluated live
    /// against every candidate on each reconcile.</param>
    /// <param name="abilityFactory">Builds a fresh batch of
    /// <see cref="IAbility"/> instances for a member on each grant. The
    /// member is passed so mana / activated / trigger closures capture the
    /// live bearer.</param>
    /// <param name="membershipProvider">Returns the live set of candidate
    /// permanents to evaluate <paramref name="scope"/> against — typically
    /// the source controller's battlefield. Re-read on every
    /// <see cref="Sync"/> so newly-entered permanents are picked up
    /// (CR 611.2c).</param>
    /// <param name="triggers">Optional live <see cref="TriggerManager"/>. When
    /// supplied, every granted ability that is an
    /// <see cref="ITriggeredAbility"/> is REGISTERED with the manager as it is
    /// granted to a member and UNREGISTERED when the grant is revoked (member
    /// left the group, or the source left play). This is what makes a
    /// group-granted TRIGGERED ability actually fire — unlike an
    /// activated / mana ability (which surfaces purely through the bearer's
    /// <see cref="Card.Abilities"/> list with no manager wiring), a triggered
    /// ability must be in the manager's evaluation set to match events. The
    /// canonical case is Kataki, War's Wage — "All artifacts have 'At the
    /// beginning of your upkeep, sacrifice this artifact unless you pay {1}.'"
    /// Null ⇒ activated / mana grants only (legacy #2322 behaviour); any
    /// granted triggered abilities are attached to the bearer but never
    /// registered (they would not fire).</param>
    public GrantAbilityToGroupStaticEffect(
        Permanent source,
        Func<Permanent, bool> scope,
        Func<Permanent, IReadOnlyList<IAbility>> abilityFactory,
        Func<IEnumerable<Permanent>> membershipProvider,
        TriggerManager? triggers = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _abilityFactory = abilityFactory ?? throw new ArgumentNullException(nameof(abilityFactory));
        _membershipProvider = membershipProvider ?? throw new ArgumentNullException(nameof(membershipProvider));
        _triggers = triggers;
    }

    public override Layer Layer => Layer.Abilities;

    public override Permanent? Source => _source;

    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    /// <summary>
    /// CR 613 — this effect "applies to" any current member of its group so
    /// the layer pass touches them; the actual ability materialisation is
    /// driven by <see cref="Sync"/> (see <see cref="GrantAbilityEffect"/>).
    /// </summary>
    public override bool AppliesTo(Permanent permanent) =>
        IsActive()
        && permanent.Zone == ZoneType.Battlefield
        && _scope(permanent);

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    // Ability grants live on the bearer's Abilities list, not on the
    // characteristics working set — Apply is intentionally a no-op (mirrors
    // GrantAbilityEffect). Reconciliation happens via Sync.
    public override void Apply(PermanentCharacteristics chars)
    {
    }

    public override void Apply(CreatureCharacteristics chars)
    {
    }

    /// <summary>The bearers currently carrying a grant from this effect.</summary>
    public IReadOnlyCollection<Permanent> Bearers => _granted.Keys;

    /// <summary>
    /// CR 613.1f / 611.2c — reconcile the live grant set with the current
    /// membership. Idempotent. Called by
    /// <see cref="ContinuousEffectsService.Compute"/> on every pass; may also
    /// be called directly by a lifecycle binder. When the source is off the
    /// battlefield every grant is revoked.
    /// </summary>
    public void Sync()
    {
        if (!IsActive())
        {
            RevokeAll();
            return;
        }

        var desired = new HashSet<Permanent>(ReferenceEqualityComparer.Instance);
        foreach (var candidate in _membershipProvider())
        {
            if (candidate == null) continue;
            if (candidate.Zone != ZoneType.Battlefield) continue;
            if (!_scope(candidate)) continue;
            desired.Add(candidate);
        }

        // Revoke grants whose bearer dropped out of the membership set
        // (CR 613.6e — when the grant ends, the granted ability is lost).
        foreach (var bearer in _granted.Keys.ToList())
        {
            if (!desired.Contains(bearer))
            {
                RevokeFrom(bearer);
            }
        }

        // Grant to newly-matching members not yet carrying this effect's
        // abilities.
        foreach (var member in desired)
        {
            if (_granted.ContainsKey(member)) continue;
            var abilities = _abilityFactory(member);
            foreach (var ability in abilities)
            {
                member.AddAbility(ability);
                // CR 603 — a group-granted TRIGGERED ability must join the live
                // TriggerManager's evaluation set to fire; the member doesn't
                // cross a zone boundary on a grant, so the manager's auto-bind
                // on CardMovedEvent never sees it. Register explicitly.
                if (_triggers != null && ability is ITriggeredAbility triggered)
                {
                    _triggers.RegisterTriggeredAbility(triggered);
                }
            }
            _granted[member] = abilities;
        }
    }

    /// <summary>
    /// Revoke every live grant. Idempotent. Called when the source leaves
    /// play or the effect is unregistered from the service.
    /// </summary>
    public void RevokeAll()
    {
        foreach (var bearer in _granted.Keys.ToList())
        {
            RevokeFrom(bearer);
        }
    }

    private void RevokeFrom(Permanent bearer)
    {
        if (!_granted.TryGetValue(bearer, out var abilities)) return;
        foreach (var ability in abilities)
        {
            bearer.RemoveAbility(ability);
            // CR 613.6e — when the grant ends the granted ability is lost; a
            // granted triggered ability must also leave the manager's set so it
            // stops matching events.
            if (_triggers != null && ability is ITriggeredAbility triggered)
            {
                _triggers.UnregisterTriggeredAbility(triggered);
            }
        }
        _granted.Remove(bearer);
    }
}
