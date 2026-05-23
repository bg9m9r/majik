using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1d / 205.3 — Layer 4 subtype-setting effect. Replaces the set
/// of subtypes that fall within <see cref="Category"/> with
/// <see cref="NewSubtypes"/>. Subtypes outside the category are
/// preserved (e.g. Blood Moon resetting land types doesn't touch
/// creature types).
///
/// Concrete example: Blood Moon — "Nonbasic lands are Mountains" —
/// constructs this with Category = LandSubtypes, NewSubtypes = { Mountain },
/// scoped via a predicate matching every nonbasic land in play.
/// </summary>
public sealed class SetSubtypesEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<Permanent, bool> _scope;

    /// <summary>
    /// CR 205.3 — the subtype category this effect rewrites. Any subtype
    /// in this set on a matched permanent is removed before
    /// <see cref="NewSubtypes"/> is added.
    /// </summary>
    public IReadOnlySet<CardSubtype> Category { get; }

    /// <summary>
    /// The subtypes the matched permanent should end up with within
    /// <see cref="Category"/>. Usually a single value (e.g. Mountain for
    /// Blood Moon, Island for Spreading Seas).
    /// </summary>
    public IReadOnlySet<CardSubtype> NewSubtypes { get; }

    public SetSubtypesEffect(
        Permanent source,
        Func<Permanent, bool> scope,
        IReadOnlySet<CardSubtype> category,
        IReadOnlySet<CardSubtype> newSubtypes)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        NewSubtypes = newSubtypes ?? throw new ArgumentNullException(nameof(newSubtypes));
    }

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        permanent.Zone == Majik.Core.Zones.ZoneType.Battlefield && _scope(permanent);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // Remove existing subtypes that fall within the category, then
        // add the new set. Subtypes outside the category are untouched.
        chars.Subtypes.RemoveWhere(Category.Contains);
        foreach (var st in NewSubtypes) chars.Subtypes.Add(st);
    }
}
