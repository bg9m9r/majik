using Majik.Core.Cards;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.62g — "If you cast a creature spell this way, it gains haste
/// until you lose control of the spell or the permanent it becomes."
/// Layer 6 keyword grant applied to a single <see cref="Creature"/> target
/// that was cast from suspend; auto-expires once the target leaves the
/// battlefield (LTB ≡ control loss for owner-controlled permanents — the
/// engine has no per-effect "control loss" lifecycle yet, so the LTB
/// gate is the conservative match).
///
/// <para>Registered on <see cref="ContinuousEffectsService"/> by
/// <see cref="Majik.Core.Game.SpellCastFlow"/> immediately after the
/// suspended creature is pushed to the stack (per CR 702.62g — the haste
/// grant attaches to "the spell" and rides into "the permanent it
/// becomes" without re-registration on resolution).</para>
///
/// <para>Self-deactivates via <see cref="IsActive"/> once
/// <see cref="Card.Zone"/> leaves the battlefield, so the next
/// <see cref="ContinuousEffectsService.Prune"/> drops it. No bus
/// subscription required.</para>
/// </summary>
public sealed class SuspendHasteEffect : ContinuousEffect
{
    private readonly Creature _target;

    public SuspendHasteEffect(Creature target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public override Layer Layer => Layer.Abilities;

    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);

    /// <summary>
    /// Active while the target is on the stack (creature spell pre-resolve)
    /// or on the battlefield (post-resolve, CR 702.62g). LTB to any other
    /// zone drops the grant — the underlying Card object becomes a
    /// "new object" on the next cast (CR 400.7) so a re-cast doesn't
    /// inherit the haste from this effect even if Card identity is reused
    /// in-process.
    /// </summary>
    public override bool IsActive()
    {
        var zone = _target.Zone;
        // Pre-resolve, the spell is on the stack — keep the grant warm
        // so it's already in place when the creature ETBs.
        return zone == ZoneType.Battlefield || zone == ZoneType.Stack;
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add("Haste");
    }
}
