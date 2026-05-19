using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.2 — Layer 2 control-changing effect (Mind Control, Threaten,
/// Act of Treason). While active, <see cref="ContinuousEffectsService.EffectiveController"/>
/// returns the new controller; the underlying <see cref="Permanent.Controller"/>
/// is left untouched so the effect's expiry restores naturally.
///
/// IsActive ties to the target permanent being on the battlefield; callers
/// that need duration semantics ("until end of turn", "until X leaves the
/// battlefield") can subclass and override <see cref="IsActive"/>.
/// </summary>
public sealed class ControlChangeEffect : ContinuousEffect
{
    public Permanent Target { get; }
    public Player NewController { get; }

    public ControlChangeEffect(Permanent target, Player newController)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        NewController = newController ?? throw new ArgumentNullException(nameof(newController));
    }

    public override Layer Layer => Layer.Control;
    public override bool AppliesTo(Creature c) => false; // not P/T-mutating
    public override bool IsActive() =>
        Target.Zone == Majik.Core.Zones.ZoneType.Battlefield;
    public override void Apply(CreatureCharacteristics chars) { /* no-op */ }
}
