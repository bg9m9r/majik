using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.7b — Layer 7b set-base P/T effect with end-of-turn expiry
/// (CR 514.2). Sibling of <see cref="BecomesPTEffect"/> for the
/// "becomes a P/T until end of turn" family — Eldrazi Mimic's printed
/// "this creature's base power and toughness become that creature's
/// power and toughness until end of turn" being the canonical caller.
/// Manlands ("becomes a 4/4 Elemental until end of turn", Mishra's
/// Factory, Faerie Conclave, ...) currently use their own per-factory
/// `*BecomesPTEffect` shims because they pair the P/T change with a
/// Layer 4 type-add; this primitive is for set-base-P/T-only riders
/// whose source already has the Creature row.
///
/// Behaviour mirrors <see cref="BecomesPTEffect"/> on the Layer-7b apply
/// side; the only delta is <see cref="ExpiresAtEndOfTurn"/> = <c>true</c>
/// so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> drops the
/// rider during the cleanup step.
/// </summary>
public sealed class BecomesPTUntilEndOfTurnEffect : ContinuousEffect
{
    private readonly Creature _target;
    public int NewPower { get; }
    public int NewToughness { get; }

    public BecomesPTUntilEndOfTurnEffect(Creature target, int power, int toughness)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        NewPower = power;
        NewToughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;
    public override bool ExpiresAtEndOfTurn => true;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }
}
