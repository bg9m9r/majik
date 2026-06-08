using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.7b — Layer 7b set-base P/T effect. "Becomes a 0/0", "Becomes
/// a 4/4 Bear creature", etc. Wipes the working P/T to the given values
/// before Layer 7c pump effects pile on top. Subtype/type changes (Layer 4)
/// are not handled here — pair with <see cref="AddSubtypeEffect"/> when
/// the same source also re-types.
/// </summary>
public sealed class BecomesPTEffect : ContinuousEffect
{
    private readonly Creature _target;
    public int NewPower { get; }
    public int NewToughness { get; }

    public BecomesPTEffect(Creature target, int power, int toughness)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        NewPower = power;
        NewToughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;
    // CR 613.1g — the target is the source for suppression purposes (the effect
    // is self-generated from the perspective of the layer service; returning the
    // target lets GameStateCloner locate this effect via the cloned permanent).
    public override Permanent? Source => _target;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="BecomesPTEffect"/> bound to
    /// <paramref name="clonedSource"/> for the search-sandbox clone.
    /// preserves: NewPower, NewToughness; target → clonedSource (as Creature).
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => clonedSource is Creature clonedCreature
            ? new BecomesPTEffect(clonedCreature, NewPower, NewToughness)
            : null;
}
