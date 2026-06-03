using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.50 — Connive. The connived creature's controller draws a card, then
/// discards a card. If a nonland card was discarded this way, that player puts
/// a +1/+1 counter on the connived creature.
///
/// "Connive X" (CR 701.50b) draws X cards, then discards X cards as a single
/// batch — the discarding player sees ALL drawn cards before choosing which to
/// discard — then puts a number of +1/+1 counters on the connived creature
/// equal to the number of NONLAND cards discarded this way.
///
/// The discard pick is the controller's choice (CR 701.50a). It is routed
/// through the controller's <see cref="IPlayerAgent.ChooseFromHandAsync"/>
/// sink (resolved via <see cref="AgentRegistry"/>, tagged
/// <see cref="BotIntent.Discard"/>) — the same declarative discard surface
/// Fable of the Mirror-Breaker's rummage (CR 701.7) and Faithless Looting use.
/// When no agent is registered (shape / direct-call tests), the deterministic
/// fallback discards the last card in hand (the just-drawn card preference),
/// preserving the legacy behaviour.
/// </summary>
public static class ConniveAction
{
    /// <summary>Connive once for <paramref name="target"/> (CR 701.50a).</summary>
    public static void Apply(Creature target) => ApplyN(target, 1);

    /// <summary>
    /// Connive X (CR 701.50b) — draw X, then discard X (batched), then put a
    /// +1/+1 counter on <paramref name="target"/> for each NONLAND card
    /// discarded this way. <paramref name="n"/> &lt;= 0 is a clean no-op.
    /// </summary>
    public static void ApplyN(Creature target, int n)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (n <= 0) return;
        var controller = target.Controller;
        if (controller == null) return;

        // ---- Draw X cards (CR 701.50b — all draws happen first). ----
        for (var i = 0; i < n; i++)
        {
            var drawn = controller.Zones.Library.GetCards().FirstOrDefault();
            if (drawn == null) break; // empty library — draw nothing further.
            controller.Zones.Library.RemoveCard(drawn);
            controller.Zones.Hand.AddCard(drawn);
            drawn.SetZone(ZoneType.Hand);
        }

        // ---- Discard X cards (CR 701.50a — the controller's choice). ----
        var agent = AgentRegistry.Get(controller);
        var nonlandDiscarded = 0;
        for (var i = 0; i < n; i++)
        {
            var hand = controller.Zones.Hand.GetCards().ToList();
            if (hand.Count == 0) break; // nothing left to discard.

            // Agent picks which card to discard; fall back to the last card in
            // hand (the just-drawn-card preference) when no agent is wired.
            ICard? pick = agent?
                .ChooseFromHandAsync(controller, hand, BotIntent.Discard)
                .GetAwaiter().GetResult();
            pick ??= hand[^1];

            controller.Zones.Hand.RemoveCard(pick);
            controller.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);

            if (!pick.HasType(CardType.Land)) nonlandDiscarded++;
        }

        // ---- +1/+1 counter per nonland card discarded (CR 701.50a/b). ----
        if (nonlandDiscarded > 0)
        {
            target.Counters.Add(CounterType.PlusOnePlusOne, nonlandDiscarded);
        }
    }
}
