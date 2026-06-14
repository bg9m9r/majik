using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613 Layer 7c — a P/T modification on a fixed creature whose duration is
/// "until your next turn" rather than "until end of turn". Unlike
/// <see cref="PumpUntilEndOfTurnEffect"/> (which the cleanup step drops via
/// <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>, CR 514.2), this effect
/// stays live across end-of-turn cleanups and is gated by an external
/// <paramref name="isActive"/> predicate. The card that created it (e.g. Fear
/// of Falling) flips the backing flag off on its controller's next turn-start,
/// at which point <see cref="ContinuousEffectsService.Prune"/> drops it.
/// </summary>
public sealed class UntilControllerNextTurnPumpEffect : ContinuousEffect
{
    private readonly Creature _target;
    private readonly int _p, _t;
    private readonly Func<bool> _isActive;

    public UntilControllerNextTurnPumpEffect(
        Creature target, int p, int t, Func<bool> isActive)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _p = p;
        _t = t;
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
    }

    public override Layer Layer => Layer.PT_Modify;

    // Duration is "until your next turn" — NOT cleared by CR 514.2 cleanup.
    public override bool ExpiresAtEndOfTurn => false;

    public override bool IsActive() => _isActive();

    // Sim-only: the cloner routes this effect to the cloned _target permanent.
    internal override Permanent? SimAnchorPermanent => _target;

    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _p;
        chars.Toughness += _t;
    }
}

/// <summary>
/// CR 613.1d / 613.6 Layer 6 — strips a single named keyword from a fixed
/// creature for "until your next turn" (NOT end of turn). The generic,
/// duration-gated sibling of <see cref="LoseKeywordUntilEndOfTurnEffect"/>:
/// where that effect is dropped by CR 514.2 cleanup, this one stays live until
/// the external <paramref name="isActive"/> predicate returns false (the
/// creating card flips its backing flag on the controller's next turn-start).
/// The keyword set under <see cref="CreatureCharacteristics.Keywords"/> is
/// ordinal-ignore-case, so casing of <paramref name="keyword"/> is irrelevant.
/// </summary>
public sealed class UntilControllerNextTurnLoseKeywordEffect : ContinuousEffect
{
    private readonly Creature _target;
    private readonly string _kw;
    private readonly Func<bool> _isActive;

    public UntilControllerNextTurnLoseKeywordEffect(
        Creature target, string keyword, Func<bool> isActive)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrWhiteSpace(keyword))
            throw new ArgumentException("Keyword required", nameof(keyword));
        _kw = keyword;
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
    }

    public override Layer Layer => Layer.Abilities;

    public override bool ExpiresAtEndOfTurn => false;

    public override bool IsActive() => _isActive();

    internal override Permanent? SimAnchorPermanent => _target;

    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Remove(_kw);
    }
}
