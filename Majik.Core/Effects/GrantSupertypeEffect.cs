using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// CR 205.4 / CR 613.1d — a Layer 4 (type-changing) continuous effect that
/// <em>grants</em> a supertype to every permanent matched by a predicate,
/// unioning it onto the supertypes the seed (and any earlier Layer-4 effect)
/// produced. This is the supertype analogue of <see cref="AddColorsEffect"/>
/// — "is legendary", "is basic", etc.
///
/// <para>Canonical consumer: the Ring's always-on emblem clause "Your
/// Ring-bearer is legendary" (CR 701.54c). The Ring-bearer designation
/// registers a <c>GrantSupertypeEffect(Legendary)</c> scoped to the current
/// bearer; the legend-rule SBA (<see cref="Majik.Core.Rules.Sba.Checks.LegendRuleCheck"/>)
/// reads the <em>effective</em> supertype set so a second same-name bearer
/// triggers the legend rule, and the grant is revoked (bearer changes /
/// leaves play) when the effect stops being active.</para>
///
/// Supertypes are added, never cleared — there is no "loses all supertypes"
/// wording in v1, so a SET variant isn't needed yet (parallels how the
/// colour-grant family ships ADD + SET but the supertype family only needs
/// ADD today).
/// </summary>
public sealed class GrantSupertypeEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<Permanent, bool> _scope;
    private readonly CardSupertype _supertype;

    /// <summary>
    /// Grant <paramref name="supertype"/> to every permanent matched by
    /// <paramref name="scope"/>. The effect is anchored to
    /// <paramref name="source"/> — it is active only while the source is on
    /// the battlefield, so a designation moving off / leaving play revokes
    /// the grant automatically (no explicit unregister needed beyond the
    /// usual battlefield gate).
    /// </summary>
    public GrantSupertypeEffect(
        Permanent source,
        Func<Permanent, bool> scope,
        CardSupertype supertype)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _supertype = supertype;
    }

    /// <summary>Convenience factory for the single-permanent "this becomes
    /// [supertype]" shape (scope = identity on <paramref name="target"/>).</summary>
    public static GrantSupertypeEffect ForPermanent(Permanent target, CardSupertype supertype) =>
        new(target, p => ReferenceEquals(p, target), supertype);

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        permanent.Zone == Majik.Core.Zones.ZoneType.Battlefield && _scope(permanent);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // CR 613.1d — ADD unions onto the seeded supertype set.
        chars.Supertypes.Add(_supertype);
    }
}
