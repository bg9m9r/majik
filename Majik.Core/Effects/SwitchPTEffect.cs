using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.7d — Layer 7d P/T switch. After all other 7-layer effects
/// have computed the working P/T, switch them. Applies last in layer 7
/// per CR 613.7d.
/// </summary>
public sealed class SwitchPTEffect : ContinuousEffect
{
    private readonly Creature _target;

    public SwitchPTEffect(Creature target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public override Layer Layer => Layer.PT_Switch;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override void Apply(CreatureCharacteristics chars)
    {
        (chars.Power, chars.Toughness) = (chars.Toughness, chars.Power);
    }
}
