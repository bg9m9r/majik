using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Primitives;

/// <summary>
/// CR 601.2 / CR 107.3 — <b>resolution-time repeatable optional mana payment</b>
/// primitive: "you may pay {cost} any number of times".
///
/// <para>
/// This is distinct from the <em>cast-time</em> repeatable additional cost
/// (<see cref="Majik.Core.Costs.MultikickerAdditionalCost"/>), whose count is
/// locked in at spell announcement (CR 601.2b) and stamped on the card as
/// <see cref="Card.TimesKicked"/>. That count is consumed by the
/// enters-with-counters <em>replacement</em> (Everflowing Chalice) and is then
/// CLEARED at battlefield entry (<see cref="Majik.Core.Services.ZoneService"/>),
/// so it is already gone by the time a creature's enters-the-battlefield
/// <em>trigger</em> resolves — see the Sea Gate Stormcaller / Goblin Bushwhacker
/// xmldoc gaps.
/// </para>
///
/// <para>
/// Bloodthirsty Adversary's "When this creature enters, you may pay {2}{R} any
/// number of times." is a different mechanic: the repeatable payment happens
/// when the ETB trigger RESOLVES, not as it is cast. There is no cast-time
/// count to read; the number of times paid is decided live, by the controller's
/// agent, during the trigger's resolution. This helper is that loop.
/// </para>
///
/// <para>
/// On each iteration it asks the controller's agent whether to pay the cost
/// again (<see cref="IPlayerAgent.ChooseYesNoAsync(string,BotIntent,System.Threading.CancellationToken)"/>),
/// then — only if they say yes AND can actually afford it — drains the mana via
/// <see cref="Player.PayMana"/>. Affordability is pre-checked against a
/// non-destructive copy of the pool (CR 601.2g — no half-payment leaks) before
/// the prompt, so a "yes" that can't be paid simply ends the loop rather than
/// leaving the pool inconsistent. The accumulated count N is returned for the
/// resolving effect to scale on.
/// </para>
/// </summary>
public static class RepeatableManaPayment
{
    /// <summary>
    /// Prompt the controller's agent to pay <paramref name="perPaymentCost"/>
    /// any number of times and return how many times it was actually paid.
    ///
    /// <para>Returns 0 when no agent / game context is wired (shape-only
    /// resolution), when the controller can't afford a single payment, or when
    /// the agent declines the first prompt.</para>
    /// </summary>
    /// <param name="controller">The player who pays (CR 117.1 — the resolving
    /// ability's controller). Their <see cref="Player.ManaPool"/> is drained.</param>
    /// <param name="agent">The deciding agent. Null ⇒ the optional payment is
    /// declined by default (0 — no count, no mana spent).</param>
    /// <param name="game">Live game context handed to the agent's prompt. Null
    /// ⇒ no live decision surface ⇒ 0.</param>
    /// <param name="perPaymentCost">The mana cost of a single payment
    /// (Bloodthirsty Adversary — {2}{R}).</param>
    /// <param name="promptText">The yes/no question shown to the agent each
    /// iteration ("Pay {2}{R} again?").</param>
    /// <param name="intent">Heuristic intent the default bot uses to answer the
    /// yes/no. Defaults to <see cref="BotIntent.Reanimate"/> | <see cref="BotIntent.Buff"/>
    /// — paying scales an upside (graveyard recursion + counters), so the
    /// no-policy bot opts in while it can afford to.</param>
    /// <param name="maxPayments">Safety ceiling on the loop (default 64) so a
    /// pathological always-yes agent against an unbounded pool can't spin
    /// forever. Real games are bounded by available mana long before this.</param>
    public static async System.Threading.Tasks.ValueTask<int> PromptAsync(
        Player controller,
        IPlayerAgent? agent,
        Majik.Core.Game.GameContext? game,
        ManaCost perPaymentCost,
        string promptText,
        BotIntent intent = BotIntent.Reanimate | BotIntent.Buff,
        int maxPayments = 64,
        System.Threading.CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(perPaymentCost);
        ArgumentNullException.ThrowIfNull(promptText);

        // No live decision surface ⇒ the optional payment is declined (0).
        if (agent == null || game == null) return 0;

        var count = 0;
        while (count < maxPayments)
        {
            // CR 601.2g — only offer the payment when it can actually be made.
            // Non-destructive affordability probe against a copy of the pool.
            var (_, affordable) = controller.ManaPool.Pay(perPaymentCost);
            if (!affordable) break;

            var again = await agent
                .ChooseYesNoAsync(promptText, intent, ct)
                .ConfigureAwait(false);
            if (!again) break;

            // Commit the payment. PayMana re-checks affordability and only
            // drains on success; the probe above means it succeeds here.
            if (!controller.PayMana(perPaymentCost)) break;
            count++;
        }

        return count;
    }
}
