using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Primitives;

/// <summary>
/// CR 603.1 / CR 117.1 — the <b>"pay {cost} unless you {consequence}"</b>
/// upkeep / delayed-trigger resolution primitive (the Pact cycle + Stasis +
/// Kataki + Mana Vault + Mana Crypt "you may pay … if you don't, …" family).
///
/// <para>
/// This is the trigger-resolution sibling of <see cref="PayUnlessCounterRider"/>
/// (the cast-time "counter unless its controller pays {N}" rider). The shape is
/// identical: at resolution the player who must pay is the resolving ability's
/// <b>controller</b> (CR 117.1 — "you" in "pay {cost}, if you don't, …"). On the
/// live async path it asks that controller's agent
/// (<see cref="IPlayerAgent.ChooseYesNoAsync(string,Cards.BotIntent,System.Threading.CancellationToken)"/>)
/// "Pay {cost}?", with a non-destructive affordability probe against a copy of
/// the pool (the SAME probe <see cref="PayUnlessCounterRider"/> /
/// <see cref="RepeatableManaPayment"/> use) BEFORE the prompt so a "yes" that
/// can't be paid is never offered (CR 601.2g — no half-payment leaks). On a
/// "yes" it commits via <see cref="Player.PayMana"/> and the consequence tail is
/// skipped; on a "no" / can't-afford / no-agent it runs the supplied
/// <c>consequence</c> (lose the game / sacrifice the source / take damage).
/// </para>
///
/// <para>
/// LEGACY / SHAPE-ONLY path (no live agent OR no game context — direct
/// <c>effect.Execute()</c> unit tests and shape-only resolves through
/// <see cref="ResolutionContext.Legacy"/>): there is no decision surface, so the
/// historical deterministic posture is preserved — the controller
/// <b>auto-pays if able</b>, otherwise the consequence fires. This keeps every
/// pre-existing factory-direct test (the pact cycle / Stasis / Kataki / Mana
/// Vault, which call the synchronous <c>Execute()</c>) green while the live
/// engine now genuinely prompts the bearer's controller.
/// </para>
///
/// <para>
/// The {2}{U}-style coloured costs the pact cycle uses are paid from whatever
/// is already in the controller's pool (the affordability probe + commit both
/// run against the live <see cref="Player.ManaPool"/>) — there is still no
/// in-trigger tap-lands step, so a controller who wants to pay must have floated
/// the mana before the trigger resolves. What this primitive closes is the
/// "may / unless" <i>decision</i>: a controller who CAN afford the cost is no
/// longer force-paid; their agent is asked and may decline into the consequence.
/// </para>
/// </summary>
public static class UpkeepPayUnlessConsequence
{
    /// <summary>
    /// Build the "pay <paramref name="cost"/> unless <paramref name="consequence"/>"
    /// resolution effect for the upkeep / delayed pact trigger of
    /// <paramref name="controller"/>.
    /// </summary>
    /// <param name="description">Human-readable effect description.</param>
    /// <param name="controller">The player who may pay (CR 117.1 — the resolving
    /// ability's controller). Their <see cref="Player.ManaPool"/> is drained on a
    /// "yes".</param>
    /// <param name="cost">The mana cost to (optionally) pay to AVOID the
    /// consequence (the Pact's {2}{B}, Stasis's {U}, Kataki's {1}, …).</param>
    /// <param name="consequence">The "if you don't" tail — run when the
    /// controller declines, cannot afford, or has no live decision surface and
    /// no mana. Lose the game / sacrifice the source / take damage.</param>
    /// <param name="promptText">The yes/no question shown to the agent. Defaults
    /// to "Pay {cost}?".</param>
    /// <param name="intent">Heuristic intent the default bot uses to answer.
    /// Defaults to <see cref="BotIntent.CostToDecline"/> — the "unless you pay X"
    /// classifier (pay only when affordable and the tax is small).</param>
    /// <param name="guard">Optional resolution-time intervening-if re-check
    /// (CR 603.4) — e.g. Mana Vault's "if this is tapped". When it returns false
    /// the whole effect no-ops (neither pays nor runs the consequence). Null ⇒
    /// always proceed.</param>
    public static IEffect Build(
        string description,
        Player controller,
        ManaCost cost,
        Action consequence,
        string? promptText = null,
        BotIntent intent = BotIntent.CostToDecline,
        Func<bool>? guard = null)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(consequence);

        var prompt = promptText ?? $"Pay {cost}?";

        return new Effect(description, async ctx =>
        {
            // CR 603.4 — re-check the printed intervening "if" at resolution.
            if (guard != null && !guard()) return;

            if (await TryPayAsync(controller, cost, intent, prompt, ctx).ConfigureAwait(false))
            {
                // Paid — the consequence is skipped.
                return;
            }

            // Declined / can't afford / no decision surface with no mana —
            // run the "if you don't" tail (lose / sacrifice / damage).
            consequence();
        });
    }

    /// <summary>
    /// Returns true when <paramref name="controller"/> pays <paramref name="cost"/>
    /// (so the caller must NOT run the consequence). Returns false when the
    /// controller is gone, can't afford it, or declines — the caller then runs
    /// the consequence.
    ///
    /// <para>On the live path (agent + game on <paramref name="ctx"/>) the
    /// controller's agent is prompted; on the legacy / shape-only path (no agent
    /// or no game) the historical "pay if able" posture is preserved.</para>
    /// </summary>
    public static async System.Threading.Tasks.ValueTask<bool> TryPayAsync(
        Player? controller,
        ManaCost cost,
        BotIntent intent,
        string prompt,
        ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(ctx);

        if (controller is null) return false;

        // CR 601.2g — non-destructive affordability probe (same probe the
        // counter rider / repeatable payment use). Can't afford ⇒ no choice to
        // make, the consequence fires.
        var (_, affordable) = controller.ManaPool.Pay(cost);
        if (!affordable) return false;

        // No live decision surface (direct Execute() / shape-only resolve) ⇒
        // preserve the deterministic "pay if able" posture so factory-direct
        // sync tests stay green.
        var agent = AgentRegistry.Get(controller);
        if (agent == null || ctx.Game == null)
        {
            return controller.PayMana(cost);
        }

        // CR 117.1 — ask the paying player. BotIntent.CostToDecline keeps the
        // heuristic bot at "pay if affordable + small"; a remote / human agent
        // receives the real "Pay {cost}?" question and decides for itself. The
        // affordability probe above guarantees a "yes" can be honoured.
        var pay = await agent
            .ChooseYesNoAsync(prompt, intent, ctx.Ct)
            .ConfigureAwait(false);

        if (!pay) return false;

        // Commit. PayMana re-checks affordability; the probe above means it
        // succeeds here.
        return controller.PayMana(cost);
    }
}
