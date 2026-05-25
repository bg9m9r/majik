using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// CR 205.2 / 613.1d — Layer 4 (type-changing) continuous effect that
/// conditionally strips <see cref="CardType.Creature"/> from a permanent
/// while a controller-supplied predicate evaluates true.
///
/// Canonical use case is the "X isn't a creature" conditional clause on
/// Theros gods (e.g. Heliod, Sun-Crowned: "As long as your devotion to
/// white is less than five, Heliod isn't a creature"). The god is a
/// printed Enchantment Creature — God; while the predicate is true the
/// effect removes the Creature type from the layered characteristics so
/// downstream consumers reading
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/>'s output see
/// only the non-creature type set. While not a creature, the permanent
/// can't be the target of creature-only spells, can't attack, can't be
/// declared as a blocker, etc. — all of which read effective-Creature via
/// the layered characteristics rather than the printed
/// <see cref="Card.CardTypes"/>.
///
/// Effect is source-anchored: <see cref="IsActive"/> is gated on
/// <see cref="Source"/> being on the battlefield, so the effect ends
/// automatically when the source LTB's. The predicate is re-evaluated on
/// every <c>Compute</c> pass, so devotion / colour counts / life totals
/// can drive the gate live without explicit register/unregister calls.
///
/// Layer-4-only — does NOT touch P/T (Layer 7), abilities (Layer 6), or
/// subtypes (Layer 4 subtype slot). Stacks alongside other Layer 4
/// effects via the service's timestamp + dependency sort
/// (CR 613.7 / 613.8). When paired with a Layer 4 ADD-Creature effect
/// (e.g. Mutavault animating into a Creature), the timestamp order
/// resolves the conflict — latest-timestamp wins per CR 613.7.
/// </summary>
public sealed class Layer4TypeStripEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<bool> _predicate;

    /// <summary>
    /// Construct a Layer-4 type-strip on <paramref name="source"/>.
    /// </summary>
    /// <param name="source">
    /// The permanent generating + targeted by this effect. The effect
    /// strips Creature from this permanent's characteristics while the
    /// predicate is true, and self-terminates when the source leaves
    /// the battlefield.
    /// </param>
    /// <param name="predicate">
    /// Re-evaluated on every <c>Compute</c> pass. When true, the strip
    /// applies and the source loses Creature type; when false the effect
    /// is a no-op (the source's printed Creature type remains visible
    /// through the layered characteristics).
    /// </param>
    public Layer4TypeStripEffect(Permanent source, Func<bool> predicate)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    /// <summary>The permanent being stripped.</summary>
    public Permanent Target => _source;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _source;

    /// <summary>
    /// Effect is source-anchored: when the source LTB's, the effect ends.
    /// The predicate gates Apply (no-op when false) rather than IsActive
    /// so the effect stays registered across predicate flickers — a
    /// devotion drop / recovery should not require re-registering the
    /// effect.
    /// </summary>
    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _source);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        if (!_predicate()) return;
        chars.Types.Remove(CardType.Creature);
    }
}
