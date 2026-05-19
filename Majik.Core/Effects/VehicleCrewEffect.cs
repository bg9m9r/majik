using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.122 — Crew N. When N or more total power of creatures you
/// control tap to crew this Vehicle, it becomes an artifact creature
/// (in addition to its other types) until end of turn.
///
/// Layer system handling: a one-turn effect that adds the Creature type
/// (Layer 4) and sets P/T (Layer 7b). MVP only sets P/T because the
/// Layer 4 type-adding path isn't wired yet; the vehicle is treated as
/// a creature for combat purposes via its <see cref="Creature"/> subclass
/// once crewed. For purposes of these tests, we just track "crewed this
/// turn" + apply P/T via the layer system.
/// </summary>
public sealed class VehicleCrewEffect : ContinuousEffect
{
    private readonly Creature _vehicle;
    private readonly int _power;
    private readonly int _toughness;

    public VehicleCrewEffect(Creature vehicle, int power, int toughness)
    {
        _vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
        _power = power;
        _toughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;
    public override bool ExpiresAtEndOfTurn => true;
    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _vehicle);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = _power;
        chars.Toughness = _toughness;
    }
}
