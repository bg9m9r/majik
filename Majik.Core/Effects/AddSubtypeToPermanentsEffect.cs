using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1d / 305.7 — Layer 4 subtype-adding effect, scoped via a
/// predicate. Adds <see cref="Subtype"/> to every matching permanent's
/// effective subtype set without disturbing existing subtypes. Differs
/// from <see cref="AddSubtypeEffect"/> in that it operates at the
/// Permanent level and accepts a predicate (multi-target) rather than a
/// single creature; differs from <see cref="SetSubtypesEffect"/> in that
/// it ADDS rather than REPLACES.
///
/// Concrete example: Urborg, Tomb of Yawgmoth — "Each land is a Swamp
/// in addition to its other types" — predicate = <c>p is Land</c>,
/// subtype = <see cref="CardSubtype.Swamp"/>. Yavimaya, Cradle of Growth
/// uses the same shape with <see cref="CardSubtype.Forest"/>.
/// </summary>
public sealed class AddSubtypeToPermanentsEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<Permanent, bool> _scope;

    /// <summary>
    /// The subtype to grant to every matching permanent. Combined
    /// additively with the permanent's existing effective subtypes.
    /// </summary>
    public CardSubtype Subtype { get; }

    public AddSubtypeToPermanentsEffect(
        Permanent source,
        Func<Permanent, bool> scope,
        CardSubtype subtype)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Subtype = subtype;
    }

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        permanent.Zone == Majik.Core.Zones.ZoneType.Battlefield && _scope(permanent);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars) =>
        chars.Subtypes.Add(Subtype);
}
