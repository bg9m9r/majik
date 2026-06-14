using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.3c characteristic-modifying continuous effect from a static
/// ability: "During your turn, this creature gets +P/+T."
///
/// Direct sibling of <see cref="WhileAttackingPumpEffect"/> — the only
/// difference is the gating predicate. Modelled as a permanent Layer 7c
/// (<see cref="Layer.PT_Modify"/>) effect whose buff is gated per-
/// <see cref="Compute"/> on a live "is it the source controller's turn?"
/// predicate rather than registered/unregistered as turns change. The
/// distinction matters: <see cref="ContinuousEffectsService.Prune"/> drops
/// any effect whose <see cref="ContinuousEffect.IsActive"/> returns false, so
/// a conditional static must keep <see cref="IsActive"/> ≡ true (it never
/// expires while the source is on the battlefield) and instead express the
/// "during your turn" condition through <see cref="AppliesTo(Creature)"/>,
/// which the service re-evaluates on every
/// <see cref="ContinuousEffectsService.Compute"/>. The buff therefore appears
/// the instant the active player becomes the source's controller (CR 500.1)
/// and lifts the instant the turn passes — exactly the static-ability
/// semantics (CR 611.2c — the effect's duration is "as long as" the condition
/// holds), not an until-end-of-turn pump.
///
/// The "is it my turn?" question is injected as a <see cref="Func{Boolean}"/>
/// so this effect stays decoupled from any specific turn-tracking object (the
/// active player rotates each turn); the predicate typically reads the live
/// active player off the game's turn state and compares it to the source's
/// controller.
///
/// Used by Skophos Reaver ("During your turn, this creature gets +2/+0").
/// </summary>
public sealed class WhileControllersTurnPumpEffect : ContinuousEffect
{
    private readonly Creature _source;
    private readonly int _power;
    private readonly int _toughness;
    private readonly Func<bool> _isControllersTurn;

    /// <summary>
    /// Build a "during your turn" pump for <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The creature that gets the buff during its
    /// controller's turn.</param>
    /// <param name="power">Power bonus (e.g. +2).</param>
    /// <param name="toughness">Toughness bonus (e.g. +0).</param>
    /// <param name="isControllersTurn">Live predicate answering "is it
    /// currently <paramref name="source"/>'s controller's turn?" — evaluated
    /// on every layer recomputation. Typically reads the active player off the
    /// game's turn state and compares it to the source's controller.</param>
    public WhileControllersTurnPumpEffect(
        Creature source,
        int power,
        int toughness,
        Func<bool> isControllersTurn)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _power = power;
        _toughness = toughness;
        _isControllersTurn = isControllersTurn
            ?? throw new ArgumentNullException(nameof(isControllersTurn));
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — this effect's source, so the layer service can
    /// suppress it if the source's abilities are stripped (Humility, etc.).</summary>
    public override Permanent? Source => _source;

    /// <summary>
    /// Never expires while the source persists — the "during your turn" gate
    /// lives in <see cref="AppliesTo(Creature)"/>, not here, so that
    /// <see cref="ContinuousEffectsService.Prune"/> doesn't permanently drop
    /// the effect when it isn't currently the controller's turn.
    /// </summary>
    public override bool IsActive() => true;

    /// <summary>
    /// CR 611.2c — applies to the source only during its controller's turn.
    /// The turn ownership is re-checked on every compute via the injected
    /// predicate.
    /// </summary>
    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source) && _isControllersTurn();

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
    }
}
