using Majik.Core.Mana;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// CR 106.4 — a cost that can be paid under a <see cref="ManaSpendContext"/>
/// (the object the mana is being spent on), so spend-restricted floating mana
/// (Eldrazi Temple, Sunken Citadel, Cavern of Souls) is honoured at the spend
/// site. Only mana costs implement this; non-mana costs (tap, sacrifice, life)
/// ignore the context and pay via the plain <see cref="ICost"/> surface.
///
/// <para><see cref="Costs.CostPayment.PayCosts(Player, System.Collections.Generic.IEnumerable{ICost}, ManaSpendContext)"/>
/// routes each cost that implements this through the context-aware overload, and
/// every other cost through the plain <see cref="ICost.Pay(Player)"/>. The
/// activated-ability path (<see cref="Services.AbilityActivator"/>) supplies an
/// <see cref="ManaSpendContext.ForAbilityCost"/> built from the ability's source
/// so the "abilities of land sources / Eldrazi" restriction half can be read.</para>
/// </summary>
public interface ISpendContextCost
{
    /// <summary>CR 106.4 — affordability under <paramref name="context"/>.</summary>
    bool CanPay(Player player, ManaSpendContext context);

    /// <summary>CR 106.4 — pay under <paramref name="context"/>.</summary>
    void Pay(Player player, ManaSpendContext context);
}
