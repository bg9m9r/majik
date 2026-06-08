using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.50 / 613.7c — Prowess pump. Layer 7c +1/+1 modification on
/// the source creature, expiring at end of turn. Registered by
/// <see cref="Abilities.TriggeredAbility"/> built for the Prowess
/// keyword in <see cref="Majik.Core.CardData.Parsing.KeywordRegistry"/>.
/// </summary>
public sealed class ProwessPumpEffect : ContinuousEffect
{
    private readonly Creature _target;

    public ProwessPumpEffect(Creature target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public override Layer Layer => Layer.PT_Modify;
    public override bool ExpiresAtEndOfTurn => true;
    // CR 613.1g — target as source so GameStateCloner can locate this effect.
    public override Permanent? Source => _target;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += 1;
        chars.Toughness += 1;
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="ProwessPumpEffect"/> bound to
    /// <paramref name="clonedSource"/> for the search-sandbox clone.
    /// The effect expires at end of turn; it is only registered mid-turn when prowess
    /// triggered, so cloning it is correct for in-turn snapshots.
    /// preserves: nothing beyond target; target → clonedSource (as Creature).
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => clonedSource is Creature clonedCreature
            ? new ProwessPumpEffect(clonedCreature)
            : null;
}
