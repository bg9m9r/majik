using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1d / 613.6 — Layer 6 ability-removing effect that strips a single
/// named keyword from a specific permanent. Narrower than
/// <see cref="LoseAllAbilitiesEffect"/>: where Humility-class effects clear
/// every keyword on every matched creature, this effect removes one keyword
/// from one designated target (typically the source's <see cref="Permanent.AttachedTo"/>,
/// e.g. Colossus Hammer's "equipped creature ... loses flying").
///
/// Like <see cref="AttachedBoostEffect"/>, the target is read dynamically
/// from the source's current attachment, so re-equipping transfers the
/// keyword-loss to the new bearer without re-registering the effect.
///
/// The keyword match is case-insensitive (<see cref="CreatureCharacteristics.Keywords"/>
/// already uses <see cref="StringComparer.OrdinalIgnoreCase"/>). Removal
/// applies to the in-flight working set only; the printed
/// <see cref="Majik.Core.Abilities.KeywordAbility"/> marker on the bearer
/// is left intact (CR 613 — continuous effects do not mutate the underlying
/// card state).
///
/// Note: this is NOT a full ability-stripper. Unlike
/// <see cref="LoseAllAbilitiesEffect"/> it does not register the bearer as
/// a stripped source in <see cref="ContinuousEffectsService"/>'s dependency
/// pre-pass, because removing a single keyword (e.g. Flying) does not
/// eliminate the creature's capacity to generate other continuous effects.
/// </summary>
public sealed class LoseKeywordEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly string _keyword;

    /// <param name="source">
    /// The permanent generating this effect (e.g. Colossus Hammer itself).
    /// The effect's target is read from <see cref="Permanent.AttachedTo"/>
    /// at evaluation time so the lookup follows re-equips.
    /// </param>
    /// <param name="keyword">Keyword to strip (case-insensitive on lookup).</param>
    public LoseKeywordEffect(Permanent source, string keyword)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(keyword))
            throw new ArgumentException("Keyword required", nameof(keyword));
        _keyword = keyword;
    }

    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield
        && _source.AttachedTo != null
        && _source.AttachedTo.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(_source.AttachedTo, creature);

    public override void Apply(CreatureCharacteristics chars)
    {
        // Strip the keyword from the working set. The HashSet uses
        // OrdinalIgnoreCase, so passing the canonical form is sufficient.
        chars.Keywords.Remove(_keyword);
    }
}
