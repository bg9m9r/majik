using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.Game;

/// <summary>
/// CR 700.6 / 701.17 — when an activated ability carries a
/// <see cref="IChooseCreatureToSacrificeCost"/> ("Sacrifice another creature" /
/// "Sacrifice a creature" as a COST, e.g. Yawgmoth, Thran Physician / Goblin
/// Bombardment), the CONTROLLER chooses which creature to sacrifice. Because
/// <see cref="ICost.Pay"/> is synchronous and has no agent, the choice can't be
/// made during payment — the activation dispatch must prompt for it FIRST and
/// stamp the chosen creature onto the cost so <see cref="ICost.Pay"/> sacrifices
/// the right one.
///
/// <para>This shared helper is called by every activation-dispatch path
/// (<c>GameFacade.DispatchActivate</c> and <c>TurnDriver.DispatchActivate</c>)
/// before <c>AbilityActivator.ActivateAbility</c> pays the costs. It reuses the
/// existing <see cref="IPlayerAgent.ChooseAsync"/> PickOne prompt (rendered by
/// the portal as a <c>ChoiceCommand</c>) — no new wire contract. Without it the
/// cost auto-picked the first eligible creature with no prompt: the live-play
/// bug.</para>
/// </summary>
public static class SacrificeCostPrompt
{
    /// <summary>
    /// For each <see cref="IChooseCreatureToSacrificeCost"/> on
    /// <paramref name="ability"/>, prompt <paramref name="agent"/> to choose
    /// which of <paramref name="actor"/>'s creatures to sacrifice and stamp the
    /// pick onto the cost. A "Sacrifice a creature" cost is mandatory once the
    /// ability is being activated (CR 602.2 — the player chose to activate), so
    /// the prompt is non-optional: the first candidate is used if the agent
    /// declines. No eligible creature leaves the choice null (the affordability
    /// gate already rejected that case upstream).
    /// </summary>
    public static async Task ChooseSacrificesAsync(
        Player actor,
        IActivatedAbility ability,
        IPlayerAgent agent,
        GameContext ctx,
        CancellationToken ct = default)
    {
        if (actor == null || ability == null || agent == null) return;

        foreach (var cost in ability.Costs)
        {
            if (cost is not IChooseCreatureToSacrificeCost sacCost) continue;

            var eligible = sacCost.EligibleSacrifices(actor);
            if (eligible.Count == 0) continue; // unaffordable — handled upstream.

            if (eligible.Count == 1)
            {
                // Only one legal choice — no need to prompt.
                sacCost.ChooseSacrifice(eligible[0]);
                continue;
            }

            var req = new ChoiceRequest(
                Kind: ChoiceKind.PickOne,
                Description: $"Choose a creature to sacrifice ({cost.Description})",
                Min: 1,
                Max: 1,
                Candidates: eligible.Cast<object>().ToList(),
                Intent: BotIntent.None,
                Optional: false);

            var chosen = await agent.ChooseAsync(ctx, req, ct).ConfigureAwait(false);
            var pick = chosen.OfType<Creature>().FirstOrDefault() ?? eligible[0];
            sacCost.ChooseSacrifice(pick);
        }
    }
}
