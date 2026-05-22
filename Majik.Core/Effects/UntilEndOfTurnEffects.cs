using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// Layer 7c +P/+T effect with end-of-turn expiry. Shared by the pump
/// template family and the composer's anaphoric-rider layer.
/// </summary>
public sealed class PumpUntilEndOfTurnEffect : ContinuousEffect
{
    private readonly Creature _target;
    private readonly int _p, _t;
    public PumpUntilEndOfTurnEffect(Creature target, int p, int t)
    { _target = target; _p = p; _t = t; }
    public override Layer Layer => Layer.PT_Modify;
    public override bool ExpiresAtEndOfTurn => true;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override void Apply(CreatureCharacteristics chars)
    { chars.Power += _p; chars.Toughness += _t; }
}

/// <summary>
/// Layer 6 keyword grant with end-of-turn expiry. Shared by the pump
/// template family and the composer's anaphoric-rider layer.
/// </summary>
public sealed class GrantKeywordUntilEndOfTurnEffect : ContinuousEffect
{
    private readonly Creature _target;
    private readonly string _kw;
    public GrantKeywordUntilEndOfTurnEffect(Creature target, string kw)
    { _target = target; _kw = kw; }
    public override Layer Layer => Layer.Abilities;
    public override bool ExpiresAtEndOfTurn => true;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override void Apply(CreatureCharacteristics chars)
    { chars.Keywords.Add(_kw); }
}
