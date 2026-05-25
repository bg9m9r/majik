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

/// <summary>
/// CR 613.1d / 613.6 — Layer 6 ability-removing effect that strips a single
/// named keyword from a specific creature until end of turn (CR 514).
/// Generic sibling of <see cref="LoseKeywordEffect"/>: where that effect
/// gates on a source permanent's <see cref="Permanent.AttachedTo"/>
/// dynamically (equipment-style "equipped creature loses X"), this one
/// targets a fixed <see cref="Creature"/> and expires in the cleanup step.
/// Used by Shadowspear's "{1}, {T}: Target creature loses indestructible
/// and hexproof until end of turn" activated ability — one effect per
/// stripped keyword.
///
/// The HashSet under <see cref="CreatureCharacteristics.Keywords"/> uses
/// <see cref="StringComparer.OrdinalIgnoreCase"/>, so casing of the
/// supplied keyword name is irrelevant.
/// </summary>
public sealed class LoseKeywordUntilEndOfTurnEffect : ContinuousEffect
{
    private readonly Creature _target;
    private readonly string _kw;
    public LoseKeywordUntilEndOfTurnEffect(Creature target, string kw)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrWhiteSpace(kw))
            throw new ArgumentException("Keyword required", nameof(kw));
        _kw = kw;
    }
    public override Layer Layer => Layer.Abilities;
    public override bool ExpiresAtEndOfTurn => true;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override void Apply(CreatureCharacteristics chars)
    { chars.Keywords.Remove(_kw); }
}
