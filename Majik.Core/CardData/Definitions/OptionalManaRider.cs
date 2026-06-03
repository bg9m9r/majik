using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// CR 601.2b / CR 603.4 — the GENERALIZED optional/reflexive "you may pay
/// {cost}. If you do, …" mana-payment rider on a triggered ability. Wraps an
/// already-materialized effect list in a single gating <see cref="IEffect"/>
/// that, at resolution, prompts the controller's agent yes/no, pays the cost
/// via <see cref="Player.PayMana"/>, and ONLY if the payment succeeds runs the
/// gated effects in printed order. A decline OR an unpayable cost skips the
/// entire "if you do" block (CR 601.2b — an optional cost that can't be paid
/// isn't paid).
///
/// <para>
/// This is the cross-verb generalization of the Obligator-specific payment that
/// was previously baked into <see cref="GainControlEffectDef.OptionalManaCost"/>:
/// it gates ANY effect list, not just <c>gain_control</c>. Target choice still
/// happens as the trigger goes on the stack (CR 603.3d) because the wrapper
/// preserves the gated effects' chosen targets — the wrapper runs each
/// sub-effect with the SAME <see cref="ResolutionContext"/>, so a wrapped
/// targeted effect reads its chosen pick exactly as it would unwrapped.
/// </para>
///
/// <para>
/// {C} (CR 107.4c colorless pip) folds into a generic pip in v1's pool model
/// (<see cref="ManaCost.Parse"/>), so e.g. <c>{1}{C}</c> is charged as two
/// generic mana — the same simplification snow ({S}) carries.
/// </para>
/// </summary>
internal static class OptionalManaRider
{
    /// <summary>
    /// Build the gating effect. The returned effect, when resolved, prompts
    /// <paramref name="controller"/>'s agent to pay <paramref name="cost"/>;
    /// on yes + payable it pays and runs <paramref name="gated"/> in order,
    /// otherwise it runs nothing and spends nothing.
    /// </summary>
    internal static IEffect Wrap(
        ICard card, Player controller, ManaCost cost, IReadOnlyList<IEffect> gated)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(gated);

        var cardName = card.Name;
        return new Effect(
            $"{cardName}: you may pay {cost}; if you do, run {gated.Count} effect(s)",
            async ctx =>
            {
                // CR 601.2b — the optional reflexive payment. Prompt yes/no; on
                // "yes" attempt the {cost} payment via the shared Player.PayMana
                // primitive. A decline OR an unpayable cost skips the ENTIRE "if
                // you do" effect block.
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                var wantsToPay = agent != null
                    && await agent.ChooseYesNoAsync(
                            ctx.Game,
                            $"Pay {cost}?",
                            cardName,
                            ctx.Ct)
                        .ConfigureAwait(false);
                if (!wantsToPay) return;
                if (!controller.ManaPool.CanPay(cost)) return;
                if (!controller.PayMana(cost)) return;

                // Payment made — run the gated effects in printed order with the
                // live resolution context (so a wrapped targeted effect reads its
                // chosen target from the same ChosenTargets slots, CR 608).
                foreach (var effect in gated)
                {
                    await effect.ExecuteAsync(ctx).ConfigureAwait(false);
                }
            });
    }
}
