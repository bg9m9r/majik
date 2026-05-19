using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1d — Layer 4 type/subtype-adding effect (Conspiracy, Arcane
/// Adaptation, Xenograft). While active, the target creature gains
/// <see cref="Subtype"/> in addition to its printed subtypes. Doesn't
/// remove existing subtypes.
/// </summary>
public sealed class AddSubtypeEffect : ContinuousEffect
{
    private readonly Creature _target;
    public CardSubtype Subtype { get; }

    public AddSubtypeEffect(Creature target, CardSubtype subtype)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        Subtype = subtype;
    }

    public override Layer Layer => Layer.Type;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Subtypes.Add(Subtype);
    }
}
