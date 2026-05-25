using Majik.Core.Costs;
using Majik.Core.Players;
using DomainRule = Majik.Core.Rules.Domain;

namespace Majik.Core.Abilities;

/// <summary>
/// CR 702.16 (Domain) + CR 117.7 — "This spell costs {N} less to cast
/// for each basic land type among lands you control." Declarative
/// cost-reduction primitive for the Domain / Coalition / WAR cycle.
///
/// Composed on top of <see cref="CostReductionAbility"/>'s whole-reducer
/// shape: each cast computes
/// <see cref="Domain.CountTypes(Player)"/> × <see cref="Multiplier"/> and
/// returns the total generic-mana reduction. Floor-at-zero and
/// coloured-pip preservation (CR 117.7c) are enforced upstream by
/// <see cref="CostReduction.GetEffectiveCost(Majik.Core.Cards.ICard, Player)"/>.
///
/// The <see cref="Multiplier"/> is the per-basic-type discount printed
/// on the card:
///   * Leyline Binding — {1} per basic land type → <c>multiplier: 1</c>.
///   * Scion of Draco — {2} per basic land type → <c>multiplier: 2</c>.
///
/// Cost-calculation runs in printed-subtypes mode (no live
/// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> threaded
/// through the cost path today — same posture as
/// <see cref="CostReductionAbility"/>'s whole-reducer overload).
/// Layer-aware Domain at resolve time (Tribal Flames under Blood Moon)
/// is still available via <see cref="Domain.CountTypes(Player,
/// Majik.Core.Effects.ContinuousEffectsService?)"/>.
/// </summary>
public sealed class DomainCostReductionAbility : CostReductionAbility
{
    /// <summary>Generic-mana reduction per distinct basic land type. {1}
    /// for Leyline Binding, {2} for Scion of Draco.</summary>
    public int Multiplier { get; }

    public DomainCostReductionAbility(int multiplier)
        : base(
            totalReducer: caster => multiplier * DomainRule.CountTypes(caster),
            description: $"Domain — costs {{{multiplier}}} less per basic land type you control")
    {
        if (multiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(multiplier),
                "Domain cost reduction multiplier must be positive (CR 117.7 — reductions are non-negative).");
        Multiplier = multiplier;
    }
}
