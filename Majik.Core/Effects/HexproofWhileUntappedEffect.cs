using Majik.Core.Cards;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.3 (Layer 6, ability-adding) — a self-applied continuous effect that
/// grants the <c>Hexproof</c> keyword (CR 702.11) to its source creature ONLY
/// while that creature is untapped.
///
/// Models Paradise Druid's "This creature has hexproof as long as it's
/// untapped." The condition is re-evaluated on every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> pass: the keyword
/// is added to the working-set characteristics only when
/// <see cref="Permanent.IsTapped"/> is false, so tapping the creature removes
/// hexproof and untapping it restores hexproof with no extra wiring.
///
/// The effect stays registered for the source's lifetime on the battlefield;
/// <see cref="IsActive"/> drops it while the source is off the battlefield so
/// the service can prune it. The untapped condition is checked inside
/// <see cref="Apply(CreatureCharacteristics)"/> rather than
/// <see cref="IsActive"/> so the effect remains attached (and re-evaluated)
/// across tap/untap transitions without being unregistered.
///
/// <see cref="Majik.Core.Targeting.TargetLegality"/> reads the computed
/// keyword set (<c>ActiveEffects.Compute(c).Keywords</c>) when the creature
/// has an <see cref="ContinuousEffectsService"/> attached, so an untapped
/// Paradise Druid can't be targeted by opponents' spells/abilities while a
/// tapped one can.
/// </summary>
public sealed class HexproofWhileUntappedEffect : ContinuousEffect
{
    private readonly Creature _source;

    public HexproofWhileUntappedEffect(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — the creature generating this effect.</summary>
    public override Permanent? Source => _source;

    /// <summary>
    /// Active while the source is on the battlefield. The untapped condition
    /// is applied in <see cref="Apply"/>, not here, so the effect stays
    /// attached and is simply re-evaluated each Compute as the creature taps
    /// and untaps.
    /// </summary>
    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        // CR 702.11 — grant Hexproof only while the source is untapped.
        if (!_source.IsTapped)
        {
            chars.Keywords.Add("Hexproof");
        }
    }
}
