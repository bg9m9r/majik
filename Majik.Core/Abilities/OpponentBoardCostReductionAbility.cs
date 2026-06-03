using Majik.Core.Costs;

namespace Majik.Core.Abilities;

/// <summary>
/// CR 117.7 — printed cost reduction whose generic-mana discount depends on
/// what an <b>opponent</b> controls. "This spell costs {N} less to cast if
/// [opponent-board condition]" / "costs {N} less for each [opponent
/// permanent]".
///
/// <para>Composed on top of <see cref="CostReductionAbility"/>'s board-aware
/// whole-reducer shape (<see cref="CostReductionAbility.ContextReducer"/>):
/// each cast hands the closure a <see cref="ReducerContext"/> (caster + full
/// player roster) so it can enumerate <see cref="ReducerContext.Opponents"/>
/// and their battlefields. This is the seam the caster-only
/// <see cref="CostReductionAbility.TotalReducer"/> (Domain) could not reach —
/// it sees only the caster's own board.</para>
///
/// <para>Floor-at-zero and coloured-pip preservation (CR 117.7c) are enforced
/// upstream by <see cref="CostReduction.GetEffectiveCost(Majik.Core.Cards.ICard,
/// Majik.Core.Players.Player, System.Collections.Generic.IEnumerable{Majik.Core.Players.Player})"/>.
/// When the cost-calc caller threads no player roster the context degrades to
/// a caster-only roster — <see cref="ReducerContext.Opponents"/> is then empty
/// and the reducer's opponent-relative count reads as zero.</para>
///
/// <para>Examples:
///   * Hagra Mauling — "costs {1} less to cast if an opponent controls no
///     basic lands" → the closure returns 1 when no opponent controls a basic
///     land, else 0.
///   * Affinity-for-opponent-permanents-style — "{1} less for each [type] an
///     opponent controls" → count matching opponent permanents × {N}.</para>
/// </summary>
public sealed class OpponentBoardCostReductionAbility : CostReductionAbility
{
    public OpponentBoardCostReductionAbility(Func<ReducerContext, int> reducer, string description)
        : base(contextReducer: reducer ?? throw new ArgumentNullException(nameof(reducer)),
               description: description)
    {
    }
}
