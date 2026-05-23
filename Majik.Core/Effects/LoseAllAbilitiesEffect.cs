using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.6 — Layer 6 ability-removing effect (e.g. Humility, Cursed Totem).
/// Removes all abilities from the in-flight <see cref="CreatureCharacteristics"/>
/// of every creature matched by <c>_predicate</c>. In this engine
/// <c>chars.Keywords</c> is the only in-characteristics ability representation,
/// so stripping reduces to <c>chars.Keywords.Clear()</c>.
///
/// Per CR 613.1d and 613.3, ability-removing effects in Layer 6 apply before
/// Layer 7 P/T modifications. Because removing a creature's abilities can
/// also disable continuous effects that the creature itself produces, the
/// <see cref="ContinuousEffectsService"/> additionally suppresses any
/// continuous effect whose <see cref="ContinuousEffect.Source"/> has been
/// stripped by an active <see cref="LoseAllAbilitiesEffect"/> (cf. CR 613.8
/// dependency-relationship handling between Layer 6 and other layers).
/// </summary>
public sealed class LoseAllAbilitiesEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly IReadOnlyList<Creature> _pool;
    private readonly Func<Creature, bool> _predicate;

    /// <param name="source">The permanent generating this effect (e.g. Humility itself).</param>
    /// <param name="pool">
    /// Snapshot of creatures the effect can scope over. In production the
    /// caller (Game) would feed the current battlefield's creatures; tests
    /// pass a fixed array.
    /// </param>
    /// <param name="predicate">
    /// Optional narrower scope. Humility = "all creatures" (default predicate).
    /// Narrower printed forms (e.g. activated-ability removal) pass a tighter
    /// filter.
    /// </param>
    public LoseAllAbilitiesEffect(
        Permanent source,
        IReadOnlyList<Creature> pool,
        Func<Creature, bool>? predicate = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _predicate = predicate ?? (_ => true);
    }

    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) =>
        creature.Zone == Majik.Core.Zones.ZoneType.Battlefield && _predicate(creature);

    public override void Apply(CreatureCharacteristics chars)
    {
        // CR 613.6 — strip all abilities. Keywords are the only in-characteristics
        // ability representation; other ability shapes (activated/triggered/static)
        // live on the Card and are gated by the same predicate when read.
        chars.Keywords.Clear();
    }

    /// <summary>
    /// Returns the set of creatures whose abilities this effect removes.
    /// Used by the layer service to compute the stripped-set without
    /// re-entering <see cref="ContinuousEffectsService.Compute"/>.
    /// </summary>
    public IEnumerable<Creature> AffectedCreatures() =>
        _pool.Where(c => c.Zone == Majik.Core.Zones.ZoneType.Battlefield && _predicate(c));
}
