using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Targeting;

namespace Majik.Core.Abilities;

/// <summary>
/// CR 708.6 — "Turn this permanent face up" activated ability granted to
/// face-down permanents whose face-down state was created by Morph
/// (CR 702.36), Manifest (CR 701.31), Manifest dread (CR 701.59), or the
/// Disguise / Cloak family (CR 702.166 / 702.167).
///
/// Distinct subclass of <see cref="ActivatedAbility"/> so
/// <see cref="Majik.Core.Cards.Permanent.EffectiveAbilities"/> can pick
/// it out as the only ability a face-down permanent exposes. Resolution
/// flips the source permanent face-up (CR 708.6); the cost is supplied
/// by the caller (typically the manifested creature card's printed mana
/// cost, or the explicit Morph / Disguise cost).
/// </summary>
public sealed class FaceDownActivatedAbility : ActivatedAbility
{
    public FaceDownActivatedAbility(
        object source,
        Player controller,
        IEnumerable<ICost>? costs = null,
        IEnumerable<IEffect>? effects = null)
        : base(
            source: source,
            controller: controller,
            targets: null,
            costs: costs,
            effects: effects,
            targetRequests: null,
            sorcerySpeed: false)
    {
    }
}
