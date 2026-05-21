using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.50 — Connive. Target creature's controller draws a card, then
/// discards a card. If a nonland card was discarded, put a +1/+1 counter
/// on the connived creature.
///
/// "Connive X" iterates X times.
///
/// V1: discard auto-picks the last card in hand (deterministic). Real
/// player choice deferred to agent prompt.
/// </summary>
public static class ConniveAction
{
    /// <summary>Connive once for the target creature.</summary>
    public static void Apply(Creature target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        var controller = target.Controller;
        if (controller == null) return;

        // Draw a card.
        var drawn = controller.Zones.Library.GetCards().FirstOrDefault();
        if (drawn != null)
        {
            controller.Zones.Library.RemoveCard(drawn);
            controller.Zones.Hand.AddCard(drawn);
            drawn.SetZone(ZoneType.Hand);
        }

        // Discard a card. V1: pick the last card in hand (most recent — likely
        // the just-drawn card if no other cards present).
        var discardPick = controller.Zones.Hand.GetCards().LastOrDefault();
        if (discardPick == null) return;
        controller.Zones.Hand.RemoveCard(discardPick);
        controller.Zones.Graveyard.AddCard(discardPick);
        discardPick.SetZone(ZoneType.Graveyard);

        // If nonland discarded, add +1/+1 counter.
        if (!discardPick.HasType(CardType.Land))
        {
            target.Counters.Add(CounterType.PlusOnePlusOne, 1);
        }
    }

    /// <summary>Connive X — apply N times.</summary>
    public static void ApplyN(Creature target, int n)
    {
        if (n <= 0) return;
        for (var i = 0; i < n; i++) Apply(target);
    }
}
