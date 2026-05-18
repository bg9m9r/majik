using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// Activates a player's chosen mana sources (lands, mana abilities), adds the
/// generated mana into the player's pool, then attempts to pay the cost
/// from the pool. Atomic: if cost can't be paid, no sources are tapped.
/// </summary>
public sealed class ManaPaymentResolver
{
    public bool Pay(Player payer, ManaCost cost, ManaPayment payment)
    {
        if (payer == null) throw new ArgumentNullException(nameof(payer));
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        if (payment == null) throw new ArgumentNullException(nameof(payment));

        var abilities = new List<IManaAbility>(payment.Sources.Count);
        foreach (var src in payment.Sources)
        {
            var ability = src.Abilities.OfType<IManaAbility>().FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"{src.Name} has no mana ability.");
            abilities.Add(ability);
        }

        // Simulate adding mana into a copy of the pool to verify the cost
        // is payable BEFORE we tap anything.
        var simulated = payer.ManaPool;
        var produced = new List<ManaCost>(abilities.Count);
        foreach (var ab in abilities)
        {
            // ManaAbility's pre-built ctor stores the cost on ManaGenerated.
            produced.Add(ab.ManaGenerated);
            simulated = simulated.Add(ab.ManaGenerated);
        }

        var (_, canPay) = simulated.Pay(cost);
        if (!canPay)
        {
            return false;
        }

        // Commit: actually tap each source and add to real pool, then pay.
        foreach (var ab in abilities)
        {
            ab.Activate();
        }
        foreach (var p in produced)
        {
            payer.AddManaToPool(p);
        }
        return payer.PayMana(cost);
    }
}
