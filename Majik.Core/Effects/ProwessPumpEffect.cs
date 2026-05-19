using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.50 / 613.7c — Prowess pump. Layer 7c +1/+1 modification on
/// the source creature, expiring at end of turn. Registered by
/// <see cref="Abilities.TriggeredAbility"/> built for the Prowess
/// keyword in <see cref="Majik.Core.CardData.Parsing.KeywordRegistry"/>.
/// </summary>
public sealed class ProwessPumpEffect : ContinuousEffect
{
    private readonly Creature _target;

    public ProwessPumpEffect(Creature target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public override Layer Layer => Layer.PT_Modify;
    public override bool ExpiresAtEndOfTurn => true;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += 1;
        chars.Toughness += 1;
    }
}
