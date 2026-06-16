using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 514.2 — Layer 7c +P/+T (or -P/-T) effect whose duration is "until
/// <see cref="ExpiryController"/>'s next turn" rather than merely until end of
/// turn. The controller-keyed sibling of <see cref="PumpUntilEndOfTurnEffect"/>:
/// it persists across the intervening opponent turn(s) and is dropped only at
/// the controller's NEXT untap step
/// (<see cref="ContinuousEffectsService.ExpireAtControllersNextUntap"/>).
///
/// <para>Vehicle: Liliana, the Last Hope's "+1: Up to one target creature gets
/// -2/-1 until your next turn" and any future "+N/+N until your next turn"
/// pump.</para>
/// </summary>
public sealed class PumpUntilControllersNextTurnEffect : ContinuousEffect
{
    private readonly Creature _target;
    private readonly int _p, _t;
    private readonly Player _controller;

    public PumpUntilControllersNextTurnEffect(Creature target, int p, int t, Player controller)
    {
        _target = target ?? throw new System.ArgumentNullException(nameof(target));
        _controller = controller ?? throw new System.ArgumentNullException(nameof(controller));
        _p = p; _t = t;
    }

    public override Layer Layer => Layer.PT_Modify;

    // CR 514.2 — controller-keyed duration; NOT end-of-turn (the two are
    // mutually exclusive — see ContinuousEffect.ExpiresAtControllersNextTurn).
    public override bool ExpiresAtControllersNextTurn => true;
    public override Player? ExpiryController => _controller;

    // Sim-only: the cloner routes this effect to the cloned _target permanent.
    internal override Permanent? SimAnchorPermanent => _target;

    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _p;
        chars.Toughness += _t;
    }

    /// <summary>
    /// Sim-only: reconstruct an identical effect bound to
    /// <paramref name="clonedSource"/>. preserves: _p, _t, _controller;
    /// target → clonedSource (as Creature). The cloned-controller lookup is
    /// best-effort — when the cloned roster isn't supplied we keep the original
    /// controller reference (sim eval only reads P/T, never the expiry key).
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Player>>? clonedPlayers)
        => clonedSource is Creature clonedCreature
            ? new PumpUntilControllersNextTurnEffect(clonedCreature, _p, _t, _controller)
            : null;
}

/// <summary>
/// CR 514.2 — Layer 6 keyword grant whose duration is "until
/// <see cref="ExpiryController"/>'s next turn". Controller-keyed sibling of
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/>. Grants one or more named
/// keywords (e.g. vigilance + reach) to a fixed creature; drops at the
/// controller's next untap step.
///
/// <para>Vehicle: Vivien, Champion of the Wilds' "+1: Until your next turn, up
/// to one target creature gains vigilance and reach".</para>
/// </summary>
public sealed class GrantKeywordsUntilControllersNextTurnEffect : ContinuousEffect
{
    private readonly Creature _target;
    private readonly string[] _keywords;
    private readonly Player _controller;

    public GrantKeywordsUntilControllersNextTurnEffect(
        Creature target, Player controller, params string[] keywords)
    {
        _target = target ?? throw new System.ArgumentNullException(nameof(target));
        _controller = controller ?? throw new System.ArgumentNullException(nameof(controller));
        if (keywords == null || keywords.Length == 0)
            throw new System.ArgumentException("At least one keyword required", nameof(keywords));
        _keywords = keywords;
    }

    public override Layer Layer => Layer.Abilities;

    public override bool ExpiresAtControllersNextTurn => true;
    public override Player? ExpiryController => _controller;

    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        foreach (var kw in _keywords) chars.Keywords.Add(kw);
    }
}
