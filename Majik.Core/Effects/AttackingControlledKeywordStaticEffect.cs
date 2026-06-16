using Majik.Core.Cards;
using Majik.Core.Combat;

namespace Majik.Core.Effects;

/// <summary>
/// Continuous keyword-grant static — "Attacking creatures you control have
/// [keyword]." (CR 702 / CR 613.1f, Layer 6.)
///
/// While the <see cref="Source"/> permanent is on the battlefield, every
/// creature that is (a) controlled by the source's controller and (b)
/// currently declared as an attacker (per the live
/// <see cref="CombatMembershipRegistry"/>) gains the granted keyword. The
/// canonical caller is Stensia Masquerade — "Attacking creatures you control
/// have first strike." — but the surface is generic over the keyword string.
///
/// <para>The attacking-membership gate is read from
/// <see cref="CombatMembershipRegistryProvider.Current"/> at compute time, the
/// same ambient surface a target-gated ability's effect closure consults — the
/// live <see cref="CombatFlow"/> writes that registry when attackers are
/// declared and clears it when combat ends, so the grant naturally applies only
/// during a combat in which the creature is attacking and lifts afterwards.
/// First strike granted before the combat-damage step is therefore in place for
/// CR 510.5 first-strike damage assignment (CR 702.7c).</para>
/// </summary>
public sealed class AttackingControlledKeywordStaticEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly string _keyword;

    public AttackingControlledKeywordStaticEffect(Permanent source, string keyword)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _keyword = string.IsNullOrWhiteSpace(keyword)
            ? throw new ArgumentException("Keyword required", nameof(keyword))
            : keyword;
    }

    // CR 613.1f — granted keywords apply in Layer 6 (ability-adding).
    public override Layer Layer => Layer.Abilities;

    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return false;
        // "you control" — CR 109.4. Read the LIVE controller of the source.
        if (!ReferenceEquals(creature.Controller, _source.Controller)) return false;
        // "attacking creatures" — CR 508.1. Consult the live combat membership.
        return CombatMembershipRegistryProvider.Current.IsAttacking(creature);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add(_keyword);
    }

    /// <summary>
    /// Sim-only reconstruction bound to the cloned source. The keyword string is
    /// the only configuration; the attacking-membership gate resolves off the
    /// ambient registry inside the clone universe, so no external resolver is
    /// needed (mirrors <see cref="LordStaticEffect.CloneForSim"/>).
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => new AttackingControlledKeywordStaticEffect(clonedSource, _keyword);
}
