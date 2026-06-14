using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;

namespace Majik.Core.Primitives;

/// <summary>
/// CR 118.4 / CR 701.5 — the <b>"counter target spell unless its controller
/// pays {N}"</b> rider (the Cancel/Force-Spike family: Mana Leak, Daze,
/// Quench, Mana Tithe, Miscalculation, Make Disappear, Metallic Rebuke, and
/// the Ghostly Prison "pay {N}" attack/cast taxes).
///
/// <para>
/// This is the <em>resolution-time single optional mana payment</em> primitive,
/// the sibling of <see cref="RepeatableManaPayment"/> (the "pay any number of
/// times" loop). The choosing player here is NOT the resolving ability's
/// controller — it is the <b>target spell's controller</b> (CR 118.4: "its
/// controller"), the player who must decide whether to pay to keep their spell
/// on the stack. So the prompt is routed to <em>that</em> player's agent,
/// looked up off <see cref="AgentRegistry"/> by the spell's controller (the
/// resolution context's <see cref="ResolutionContext.Agent"/> is the
/// COUNTERSPELL caster's agent, the wrong seat to ask).
/// </para>
///
/// <para>
/// On the live async path it asks the paying player's agent
/// (<see cref="IPlayerAgent.ChooseYesNoAsync(string,Cards.BotIntent,System.Threading.CancellationToken)"/>)
/// "Pay {N} to keep this spell on the stack?", with a non-destructive
/// affordability probe against a copy of the pool (the SAME probe
/// <see cref="RepeatableManaPayment"/> uses) BEFORE the prompt so a "yes" that
/// can't be paid is never offered (CR 601.2g — no half-payment leaks). On a
/// "yes" it commits via <see cref="Player.PayMana"/> and the counter no-ops
/// (the spell survives); on a "no" / can't-afford / no-agent it counters via
/// <see cref="Fx.Counter"/> (uncounterable spells survive per CR 701.5b).
/// </para>
///
/// <para>
/// LEGACY / SHAPE-ONLY path (no live agent OR no game context — direct
/// <c>EffectFactory(...)[i].Execute()</c> unit tests and shape-only resolves):
/// there is no decision surface, so the historical deterministic posture is
/// preserved — the controller <b>auto-pays if able</b>. This keeps every
/// pre-existing factory-direct test (Spell Pierce / Mana Tithe / Quench / …,
/// which call the synchronous <c>Execute()</c>) green while the live engine
/// now genuinely prompts.
/// </para>
/// </summary>
public static class PayUnlessCounterRider
{
    /// <summary>
    /// Build the resolution effect for "counter <paramref name="resolveTarget"/>
    /// unless its controller pays {<paramref name="unlessPayN"/>}".
    /// </summary>
    /// <param name="description">The effect's human-readable description.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.
    /// Null ⇒ the effect is a clean no-op (shape-only build).</param>
    /// <param name="resolveTarget">Resolves the live target spell at resolution
    /// (closes over the chosen-target token + the binder's resolver). Null ⇒
    /// no-op.</param>
    /// <param name="unlessPayN">The generic mana the controller may pay to keep
    /// the spell (CR 118.4). 0 ⇒ no pay rider (always counter).</param>
    public static IEffect Build(
        string description,
        Majik.Core.Stack.Stack? stack,
        Func<ISpell?> resolveTarget,
        int unlessPayN)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(resolveTarget);

        return new Effect(description, async ctx =>
        {
            var spell = resolveTarget();
            if (stack == null || spell is null) return;

            var controller = spell.Controller;
            if (await TryPayUnlessAsync(controller, unlessPayN, ctx).ConfigureAwait(false))
            {
                // Paid — the counter no-ops; the spell stays on the stack.
                return;
            }

            // Not paid (declined / can't afford / no decision surface) —
            // counter the spell (CR 701.5; uncounterable spells survive).
            Fx.Counter(stack, spell);
        });
    }

    /// <summary>
    /// Returns true when the <paramref name="controller"/> pays {N} to keep
    /// their spell (so the caller must NOT counter). Returns false when the
    /// rider has no pay clause, the controller is gone, can't afford it, or
    /// declines — the caller then counters.
    /// </summary>
    public static async System.Threading.Tasks.ValueTask<bool> TryPayUnlessAsync(
        Player? controller,
        int unlessPayN,
        ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (unlessPayN <= 0 || controller is null) return false;

        var cost = ManaCost.Zero.AddGenericCost(unlessPayN);

        // CR 601.2g — non-destructive affordability probe (same probe
        // RepeatableManaPayment uses). Can't afford ⇒ no choice to make.
        var (_, affordable) = controller.ManaPool.Pay(cost);
        if (!affordable) return false;

        // Route the prompt to the PAYING player's agent. The resolution
        // context's Agent is the COUNTERSPELL caster's seat, so look up the
        // target spell's controller's agent off the per-game registry. No
        // agent / no game ⇒ shape-only resolve ⇒ deterministic "pay if able"
        // (preserves the legacy synchronous posture).
        var payingAgent = AgentRegistry.Get(controller);
        if (payingAgent == null || ctx.Game == null)
        {
            return controller.PayMana(cost);
        }

        // CR 118.4 — ask the paying player. The intent-carrying overload is
        // the one the live HeuristicBotAgent / ScriptedAgent / remote UI agents
        // implement. BotIntent.None keeps the default heuristic bot at the
        // historical "pay-if-able" posture (neutral intent ⇒ accept), while a
        // remote / human agent receives the real "Pay {N}?" question text and
        // decides for itself. The affordability probe above guarantees a "yes"
        // can be honoured.
        var pay = await payingAgent
            .ChooseYesNoAsync(
                $"Pay {cost} to keep your spell on the stack?",
                BotIntent.None,
                ctx.Ct)
            .ConfigureAwait(false);

        if (!pay) return false;

        // Commit. PayMana re-checks affordability; the probe above means it
        // succeeds here.
        return controller.PayMana(cost);
    }
}
