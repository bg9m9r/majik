using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1f keyword-granting continuous effect from a static ability:
/// "[This creature] has [keyword] during your turn."
///
/// The Layer 6 (ability-adding) sibling of
/// <see cref="WhileControllersTurnPumpEffect"/> — the only differences are the
/// layer (abilities vs P/T) and the payload (a keyword string vs a P/T delta).
/// Like the pump sibling, this is modelled as a <i>permanent</i> effect whose
/// "during your turn" condition lives in <see cref="AppliesTo(Creature)"/>, not
/// in <see cref="IsActive"/>: <see cref="ContinuousEffectsService.Prune"/> drops
/// any effect whose <see cref="ContinuousEffect.IsActive"/> returns false, so a
/// conditional static must keep <see cref="IsActive"/> ≡ true (it never expires
/// while the source is on the battlefield) and express the duration condition
/// through <see cref="AppliesTo(Creature)"/>, which the service re-evaluates on
/// every <see cref="ContinuousEffectsService.Compute"/>. The keyword therefore
/// appears the instant the active player becomes the source's controller
/// (CR 500.1) and lifts the instant the turn passes — exactly the
/// static-ability "as long as" semantics (CR 611.2c), not an until-end-of-turn
/// grant.
///
/// The "is it my turn?" question is injected as a <see cref="Func{Boolean}"/> so
/// this effect stays decoupled from any specific turn-tracking object (the
/// active player rotates each turn); the predicate typically reads the live
/// active player off the game's turn state and compares it to the source's
/// controller.
///
/// Used by Razorkin Needlehead ("This creature has first strike during your
/// turn.").
/// </summary>
public sealed class WhileControllersTurnKeywordStaticEffect : ContinuousEffect
{
    private readonly Creature _source;
    private readonly string _keyword;
    private readonly Func<bool> _isControllersTurn;

    /// <summary>
    /// Build a "during your turn" keyword grant for <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The creature that gains the keyword during its
    /// controller's turn.</param>
    /// <param name="keyword">The keyword to grant (e.g. "First strike"). Must
    /// match the casing the combat keyword lookups use
    /// (<see cref="Majik.Core.Combat.CombatAbilities"/>).</param>
    /// <param name="isControllersTurn">Live predicate answering "is it currently
    /// <paramref name="source"/>'s controller's turn?" — evaluated on every layer
    /// recomputation. Typically reads the active player off the game's turn state
    /// and compares it to the source's controller.</param>
    public WhileControllersTurnKeywordStaticEffect(
        Creature source,
        string keyword,
        Func<bool> isControllersTurn)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _keyword = string.IsNullOrWhiteSpace(keyword)
            ? throw new ArgumentException("Keyword required", nameof(keyword))
            : keyword;
        _isControllersTurn = isControllersTurn
            ?? throw new ArgumentNullException(nameof(isControllersTurn));
    }

    // CR 613.1f — granted keywords apply in Layer 6 (ability-adding).
    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — this effect's source, so the layer service can
    /// suppress it if the source's abilities are stripped (Humility, etc.).</summary>
    public override Permanent? Source => _source;

    /// <summary>
    /// Never expires while the source persists — the "during your turn" gate
    /// lives in <see cref="AppliesTo(Creature)"/>, not here, so that
    /// <see cref="ContinuousEffectsService.Prune"/> doesn't permanently drop the
    /// effect when it isn't currently the controller's turn.
    /// </summary>
    public override bool IsActive() => true;

    /// <summary>
    /// CR 611.2c — applies to the source only during its controller's turn. The
    /// turn ownership is re-checked on every compute via the injected predicate.
    /// </summary>
    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source) && _isControllersTurn();

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add(_keyword);
    }

    /// <summary>
    /// Sim-only reconstruction bound to the cloned source. The "is it my turn?"
    /// gate is rebuilt to read the cloned creature's controller off the cloned
    /// turn state (the predicate captured here closes over the original service /
    /// players, which don't exist in the clone universe). The
    /// <paramref name="clonedPlayers"/> provider supplies the cloned player list;
    /// the active player is identified by index parity against the original — but
    /// since the clone preserves turn ownership through
    /// <c>clonedSource.Controller</c>, the predicate compares the cloned active
    /// player to the cloned controller. Returns null if the cloned source is not
    /// a creature (keyword grants target creatures only).
    /// preserves: _keyword; source → clonedSource (as Creature); the
    /// "controller's turn" predicate is reconstructed against the clone universe.
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
    {
        if (clonedSource is not Creature clonedCreature) return null;
        // The clone carries no live turn state on its own; the cloner re-points
        // ActiveEffects/turn state. Re-evaluate ownership by reading the cloned
        // creature's controller against the cloned active player when available;
        // when no provider is supplied, fall back to "never active" so the clone
        // is conservative rather than over-buffing.
        return new WhileControllersTurnKeywordStaticEffect(
            clonedCreature,
            _keyword,
            isControllersTurn: () =>
            {
                var effects = clonedCreature.ActiveEffects;
                return effects?.ActivePlayer != null
                    && ReferenceEquals(effects.ActivePlayer, clonedCreature.Controller);
            });
    }
}
