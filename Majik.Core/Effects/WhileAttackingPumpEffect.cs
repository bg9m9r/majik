using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.3c characteristic-modifying continuous effect from a static
/// ability: "As long as this creature is attacking, it gets +P/+T."
///
/// Modelled as a permanent Layer 7c (<see cref="Layer.PT_Modify"/>) effect
/// whose buff is gated per-<see cref="Compute"/> on a live "is attacking"
/// predicate rather than registered/unregistered as combat starts and ends.
/// The distinction matters: <see cref="ContinuousEffectsService.Prune"/>
/// drops any effect whose <see cref="ContinuousEffect.IsActive"/> returns
/// false, so a conditional static must keep <see cref="IsActive"/> ≡ true
/// (it never expires while the source is on the battlefield) and instead
/// express the "as long as attacking" condition through
/// <see cref="AppliesTo(Creature)"/>, which the service re-evaluates on
/// every <see cref="ContinuousEffectsService.Compute"/>. The buff therefore
/// appears the instant the creature is declared as an attacker (CR 508.1)
/// and lifts the instant it leaves combat — exactly the static-ability
/// semantics (CR 611.2c — the effect's duration is "as long as" the
/// condition holds), not an until-end-of-turn pump.
///
/// The "is attacking" question is injected as a <see cref="Func{Boolean}"/>
/// so this effect stays decoupled from a specific
/// <see cref="Majik.Core.Combat.Combat"/> instance (a fresh
/// <c>Combat</c> object is created each combat phase); the predicate
/// typically reads the supplied combat manager's current attacker set.
///
/// Used by Adanto Vanguard ("As long as this creature is attacking, it gets
/// +2/+0").
/// </summary>
public sealed class WhileAttackingPumpEffect : ContinuousEffect
{
    private readonly Creature _source;
    private readonly int _power;
    private readonly int _toughness;
    private readonly Func<Creature, bool> _isAttacking;

    /// <summary>
    /// Build a "while attacking" pump for <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The creature that gets the buff while it attacks.</param>
    /// <param name="power">Power bonus (e.g. +2).</param>
    /// <param name="toughness">Toughness bonus (e.g. +0).</param>
    /// <param name="isAttacking">Live predicate answering "is
    /// <paramref name="source"/> currently attacking?" — evaluated on every
    /// layer recomputation. Typically reads the combat manager's current
    /// attacker set.</param>
    public WhileAttackingPumpEffect(
        Creature source,
        int power,
        int toughness,
        Func<Creature, bool> isAttacking)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _power = power;
        _toughness = toughness;
        _isAttacking = isAttacking ?? throw new ArgumentNullException(nameof(isAttacking));
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — this effect's source, so the layer service can
    /// suppress it if the source's abilities are stripped (Humility, etc.).</summary>
    public override Permanent? Source => _source;

    /// <summary>
    /// Never expires while the source persists — the "as long as attacking"
    /// gate lives in <see cref="AppliesTo(Creature)"/>, not here, so that
    /// <see cref="ContinuousEffectsService.Prune"/> doesn't permanently drop
    /// the effect when the source isn't currently attacking.
    /// </summary>
    public override bool IsActive() => true;

    /// <summary>
    /// CR 611.2c — applies to the source only while it is attacking. Combat
    /// membership is re-checked on every compute via the injected predicate.
    /// </summary>
    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source) && _isAttacking(_source);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
    }
}
