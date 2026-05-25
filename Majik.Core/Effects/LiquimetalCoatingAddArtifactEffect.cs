using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// Liquimetal Coating — "{T}: Target permanent becomes an artifact in
/// addition to its other types until end of turn."
///
/// CR 613.1d — Layer 4 (type-changing). Adds <see cref="CardType.Artifact"/>
/// to the target permanent's effective types for the remainder of the
/// turn. "In addition to its other types" is satisfied by the ADD
/// semantic of <see cref="PermanentCharacteristics.Types"/> (the printed
/// types remain present at Compute time). Mirrors
/// <see cref="KarnAnimateArtifactEffect"/>'s Layer 4 type-add shape.
///
/// Note (v1): <see cref="ContinuousEffectsService.Compute(Permanent)"/>
/// builds a <see cref="CreatureCharacteristics"/> when the runtime C# type
/// is <see cref="Creature"/> and a bare <see cref="PermanentCharacteristics"/>
/// otherwise — same posture as KarnAnimateArtifactEffect. Either way the
/// Layer 4 ADD applies to <see cref="PermanentCharacteristics.Types"/>,
/// which is what "becomes an artifact in addition to its other types"
/// cares about for downstream rules consumers (Shatter / Naturalize-style
/// effects keying off the Artifact type, etc.).
/// </summary>
public sealed class LiquimetalCoatingAddArtifactEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public LiquimetalCoatingAddArtifactEffect(Permanent target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The permanent being made an artifact.</summary>
    public Permanent Target => _target;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // ADD — "in addition to its other types". HashSet semantics make
        // this a no-op if the target was already an artifact.
        chars.Types.Add(CardType.Artifact);
    }
}
