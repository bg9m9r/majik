using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// CR 603.4 — the GENERALIZED <b>free</b> optional "you may [effect]" rider on a
/// triggered ability (no payment, just a yes/no choice). Wraps an
/// already-materialized effect list in a single gating <see cref="IEffect"/>
/// that, at resolution, prompts the controller's agent yes/no and ONLY on "yes"
/// runs the gated effects in printed order. A decline runs nothing.
///
/// <para>
/// This is the cost-free sibling of <see cref="OptionalManaRider"/> (which gates
/// behind a mana payment). It models the bare "you may …" reflexive clause —
/// Mortician Beetle's "Whenever a player sacrifices a creature, you may put a
/// +1/+1 counter on this creature", Pawn of Ulamog's "you may create …", and the
/// broad "you may [do something]" trigger family — without baking the choice
/// into each verb. The wrapper runs each sub-effect with the SAME
/// <see cref="ResolutionContext"/>, so a wrapped targeted effect reads its
/// chosen pick exactly as it would unwrapped (CR 603.3d — targets are still
/// chosen as the trigger goes on the stack, independent of the later yes/no).
/// </para>
///
/// <para>
/// When no agent is registered for the controller (a direct-construction unit
/// test, or a pure-shape build), the choice cannot be made — the rider is a
/// clean no-op (it runs nothing). Live games always have an agent.
/// </para>
/// </summary>
internal static class FreeOptionalRider
{
    /// <summary>
    /// Build the gating effect. The returned effect, when resolved, prompts
    /// <paramref name="controller"/>'s agent yes/no; on "yes" it runs
    /// <paramref name="gated"/> in printed order, otherwise it runs nothing.
    /// </summary>
    internal static IEffect Wrap(
        ICard card, Player controller, IReadOnlyList<IEffect> gated)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(gated);

        var cardName = card.Name;
        return new Effect(
            $"{cardName}: you may run {gated.Count} effect(s)",
            async ctx =>
            {
                // CR 603.4 — the free optional choice. Prompt yes/no; on "yes"
                // run the gated effects, on "no" (or no agent) run nothing.
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                if (agent is null) return;
                var wantsTo = await agent
                    .ChooseYesNoAsync(ctx.Game, $"{cardName}: do it?", cardName, ctx.Ct)
                    .ConfigureAwait(false);
                if (!wantsTo) return;

                foreach (var effect in gated)
                {
                    await effect.ExecuteAsync(ctx).ConfigureAwait(false);
                }
            });
    }
}
