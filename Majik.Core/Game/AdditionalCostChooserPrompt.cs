using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.Game;

/// <summary>
/// CR 601.2f / CR 601.2h — when a SPELL carries a chooser-bearing non-mana
/// additional cost (<see cref="IChooseAdditionalCostPayment"/> — a typed
/// sacrifice such as Deadly Dispute's "sacrifice an artifact or creature", or a
/// variable discard such as Nahiri's Wrath's "discard X cards"), the CONTROLLER
/// chooses which object(s) the cost consumes. Because
/// <see cref="IAdditionalCost.Pay"/> is synchronous and carries no agent, the
/// choice can't be made during payment — the cast pipeline prompts for it FIRST
/// and stamps the pick onto the cost so <see cref="IAdditionalCost.Pay"/>
/// consumes exactly the chosen object(s).
///
/// <para>This is the SPELL-cast analogue of <see cref="SacrificeCostPrompt"/>
/// (which handles the activated-ability <see cref="IChoosePermanentToSacrificeCost"/>
/// / <see cref="IChooseCreatureToSacrificeCost"/> pair). It is invoked by
/// <see cref="SpellCastFlow"/> at the CR 601.2h payment point — AFTER target
/// choice (CR 601.2c) and immediately before <c>PayAdditionalCosts</c> — so a
/// targeting failure rewinds the cast (CR 731.1) without ever prompting the
/// chooser, and the same declarative <see cref="IPlayerAgent.ChooseAsync"/> sink
/// the portal already renders as a <c>ChoiceCommand</c> is reused (no new wire
/// contract). Without it these costs silently auto-picked (first-eligible
/// permanent / the whole hand) — the live-play bug this prompt fixes.</para>
/// </summary>
public static class AdditionalCostChooserPrompt
{
    /// <summary>
    /// For each <see cref="IChooseAdditionalCostPayment"/> in
    /// <paramref name="mergedAdditional"/>, ask <paramref name="agent"/> which
    /// object(s) the cost consumes (when the cost warrants a prompt — see
    /// <see cref="IChooseAdditionalCostPayment.BuildChoiceRequest"/>) and stamp
    /// the pick via <see cref="IChooseAdditionalCostPayment.ApplyChoice"/>.
    /// A non-chooser additional cost is left untouched.
    /// </summary>
    public static async Task PromptForChoicesAsync(
        IReadOnlyList<IAdditionalCost> mergedAdditional,
        Player caster,
        IPlayerAgent agent,
        GameContext ctx,
        CancellationToken ct = default)
    {
        if (mergedAdditional == null || caster == null || agent == null) return;

        foreach (var cost in mergedAdditional)
        {
            if (cost is not IChooseAdditionalCostPayment chooser) continue;

            var request = chooser.BuildChoiceRequest(caster);
            if (request == null) continue; // no prompt (forced / pre-supplied).

            var chosen = await agent.ChooseAsync(ctx, request, ct).ConfigureAwait(false);
            chooser.ApplyChoice(chosen);
        }
    }
}
