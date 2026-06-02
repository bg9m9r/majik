using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// CR 706.2 / 613.1d — Layer 4 (type-changing) continuous effect that
/// <em>removes</em> a <see cref="CardSupertype"/> from a permanent's effective
/// supertype set. This is the strip analogue of
/// <see cref="GrantSupertypeEffect"/>.
///
/// <para>Canonical consumer: the "except it's not legendary if that [permanent]
/// is legendary" clause on copy effects (CR 706.2) — Vesuva, Spark Double. A
/// <see cref="CopyCharacteristicsEffect"/> (Layer 1) copies the source's
/// supertypes, including <see cref="CardSupertype.Legendary"/>; this Layer-4
/// effect runs afterward (Layer 4 &gt; Layer 1) and strips Legendary so the
/// copy is never legendary. The legend-rule SBA reads
/// <see cref="Permanent.HasEffectiveSupertype"/>, which consults this through
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/>, so the copy does
/// not trigger the legend rule against the legendary original.</para>
///
/// Effect is source-anchored: it applies only while the target is on the
/// battlefield, so it ends automatically when the target leaves play.
/// </summary>
public sealed class RemoveSupertypeEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The supertype removed from the target's effective set.</summary>
    public CardSupertype Supertype { get; }

    public RemoveSupertypeEffect(Permanent target, CardSupertype supertype)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        Supertype = supertype;
    }

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);

    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // CR 706.2 — strip the supertype the Layer-1 copy seeded from the source.
        chars.Supertypes.Remove(Supertype);
    }
}
