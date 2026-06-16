using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1f / 702 — a Layer-6 ability-adding continuous static that grants a
/// keyword to every creature its source's controller controls THAT IS CURRENTLY
/// ATTACKING. The canonical case is Stensia Masquerade — "Attacking creatures
/// you control have first strike." — but the same shape covers any
/// "attacking creatures you control have &lt;keyword&gt;" anthem.
///
/// <para>Unlike <see cref="GrantAbilityToGroupStaticEffect"/> (which
/// materialises a <see cref="Majik.Core.Abilities.KeywordAbility"/> marker on the
/// bearer's <see cref="Card.Abilities"/> list via <c>AddAbility</c> during
/// <see cref="ContinuousEffectsService"/>'s pre-pass reconcile — a marker that is
/// only baked into the computed keyword set on the NEXT <c>Compute</c> pass), this
/// effect adds the keyword DIRECTLY to the working
/// <see cref="CreatureCharacteristics.Keywords"/> set in its <see cref="Apply"/>
/// during the layer walk, exactly like the single-target
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/>. So a creature has the granted
/// keyword in the SAME <c>Compute</c> call in which it is found to be attacking —
/// no two-pass settle. This matters because combat membership changes WITHOUT a
/// zone move (an attacker is declared mid-combat — CR 508.1), so the grant must
/// surface the instant <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/>
/// (which reads <c>Compute(creature).Keywords</c>) is consulted.</para>
///
/// <para>Membership is the LIVE per-game combat registry
/// (<see cref="CombatMembershipRegistryProvider.Current"/>): a creature is a
/// member iff it is a declared attacker (<see cref="CombatMembershipRegistry.IsAttacking"/>)
/// — CR 508.4: a creature is "attacking" from declaration until it leaves combat
/// or combat ends (CR 511.3). The grant is alive only while the
/// <see cref="Source"/> enchantment is on the battlefield (CR 611.2c).</para>
/// </summary>
public sealed class AttackingCreaturesKeywordAnthemEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly string _keyword;

    /// <param name="source">CR 613.1g — the permanent generating the anthem
    /// (Stensia Masquerade). The grant is alive only while it is on the
    /// battlefield.</param>
    /// <param name="keyword">The keyword granted to each attacking creature the
    /// source's controller controls (e.g. <c>"First strike"</c>, CR 702.7).</param>
    public AttackingCreaturesKeywordAnthemEffect(Permanent source, string keyword)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _keyword = string.IsNullOrWhiteSpace(keyword)
            ? throw new ArgumentException("Keyword required", nameof(keyword))
            : keyword;
    }

    public override Layer Layer => Layer.Abilities;

    public override Permanent? Source => _source;

    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    /// <summary>
    /// CR 613 — applies to a creature on the battlefield that the source's
    /// controller controls AND that is currently an attacker (CR 508.4). The
    /// attacking test reads the live per-game combat membership registry.
    /// </summary>
    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent)
    {
        if (!IsActive()) return false;
        if (permanent is not Creature) return false;
        if (permanent.Zone != ZoneType.Battlefield) return false;
        // "you control" (CR 109.5) — scoped to the source's controller.
        if (!ReferenceEquals(permanent.Controller, _source.Controller)) return false;
        // "attacking" (CR 508.4) — live combat membership.
        return CombatMembershipRegistryProvider.Current.IsAttacking(permanent);
    }

    /// <summary>
    /// CR 702 — add the granted keyword to the creature's computed keyword set
    /// for this layer pass. Mirrors <see cref="GrantKeywordUntilEndOfTurnEffect"/>
    /// (Layer-6 keyword add via direct <c>chars.Keywords</c> mutation), so the
    /// keyword surfaces in the same <c>Compute</c> call that found the creature
    /// attacking.
    /// </summary>
    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add(_keyword);
    }
}
