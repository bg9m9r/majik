using System.Linq;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sphere of Safety (Return to Ravnica).
///
/// Enchantment — {4}{W}. Oracle text:
///   "Creatures can't attack you or planeswalkers you control unless their
///    controller pays {X} for each of those creatures, where X is the number
///    of enchantments you control."
///
/// The dynamic-cost / planeswalker-protecting variant of the attack-tax
/// paywall (CR 508.1g). The per-attacker tax is {X} where X is the number of
/// enchantments the controller controls, recomputed at declare-attackers (so
/// it scales with the board — and Sphere of Safety counts itself). It protects
/// both the controller AND the planeswalkers they control. See
/// <see cref="AttackTaxPaywallBuilder"/>.
/// </summary>
[CardName("Sphere of Safety")]
public static class SphereOfSafetyFactory
{
    public const string CardName = "Sphere of Safety";
    public const string PrintedManaCost = "{4}{W}";

    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return AttackTaxPaywallBuilder.BuildDynamic(
            CardName,
            PrintedManaCost,
            owner,
            genericPerAttacker: controller => CountEnchantments(controller),
            protectsPlaneswalkers: true);
    }

    /// <summary>"the number of enchantments you control" — counts every
    /// battlefield permanent the controller controls that has the Enchantment
    /// card type (so Enchantment Creatures / Enchantment Lands count too, CR
    /// 205.2a), including Sphere of Safety itself.</summary>
    private static int CountEnchantments(Player controller) =>
        controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Count(p => p.HasType(CardType.Enchantment));
}
