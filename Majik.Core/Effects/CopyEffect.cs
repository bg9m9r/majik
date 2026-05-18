using Majik.Core.Abilities;
using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 706 / CR 613.7a — Layer 1 copy effect. The "copier" creature takes
/// on the copiable values of the "original" creature: name (out of scope
/// for this MVP), printed P/T, and printed keyword abilities.
///
/// Implementation note: writes BasePower/BaseToughness and keyword set
/// into the working <see cref="CreatureCharacteristics"/>; downstream
/// layers (7c modify, counters) still apply on top.
/// </summary>
public sealed class CopyEffect : ContinuousEffect
{
    private readonly Creature _copier;
    private readonly Creature _original;

    public CopyEffect(Creature copier, Creature original)
    {
        _copier = copier ?? throw new ArgumentNullException(nameof(copier));
        _original = original ?? throw new ArgumentNullException(nameof(original));
    }

    public override Layer Layer => Layer.Copy;
    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _copier);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = _original.BasePower;
        chars.Toughness = _original.BaseToughness;
        chars.Keywords.Clear();
        foreach (var kw in _original.Abilities.OfType<KeywordAbility>())
        {
            chars.Keywords.Add(kw.Keyword);
        }
    }
}
