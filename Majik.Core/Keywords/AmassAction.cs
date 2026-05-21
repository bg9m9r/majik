using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.49 — Amass [tribe] N.
/// 1. If the player controls one or more Army creatures, choose one
///    (v1: auto-pick first Army found on battlefield).
/// 2. Otherwise create a 0/0 black [tribe] Army creature token.
/// 3. Put N +1/+1 counters on that creature.
/// </summary>
public static class AmassAction
{
    /// <summary>
    /// Execute Amass [<paramref name="tribe"/>] <paramref name="count"/> for
    /// <paramref name="controller"/>.
    /// Returns the Army creature that received the counters (token or existing).
    /// </summary>
    public static Creature Apply(
        Player controller,
        int count,
        CardSubtype tribe,
        ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (count <= 0) throw new ArgumentException("count must be positive", nameof(count));

        // CR 701.49a: if the player controls one or more Army creatures, choose one.
        // v1: auto-pick the first Army creature on the battlefield.
        Creature? army = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.Subtypes.Contains(CardSubtype.Army));

        // CR 701.49b: otherwise create a 0/0 black [tribe] Army creature token.
        if (army == null)
        {
            army = TokenFactory.CreateArmy(controller, tribe, zones);
        }

        // CR 701.49c: put N +1/+1 counters on that creature.
        army.Counters.Add(CounterType.PlusOnePlusOne, count);

        return army;
    }
}
