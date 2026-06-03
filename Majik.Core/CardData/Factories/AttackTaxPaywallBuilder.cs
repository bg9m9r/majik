using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Shared builder for the "attack-tax paywall" enchantment family (CR 508.1g —
/// Ghostly Prison / Propaganda / Sphere of Safety): "Creatures can't attack you
/// [or planeswalkers you control] unless their controller pays {cost} for each
/// creature attacking you."
///
/// Builds the <see cref="Enchantment"/> and registers a
/// <see cref="PayPerAttackerRestriction"/> on the per-game
/// <see cref="AttackRestrictionRegistryProvider"/>. The restriction protects
/// the enchantment's CONTROLLER (CR 109.5 — "you") and is gated on the
/// enchantment still being on the battlefield, so it auto-deactivates when the
/// enchantment leaves (no LTB unregister — mirrors Static Prison's
/// zone-guarded replacement). <see cref="CombatFlow"/> charges the per-attacker
/// cost at declare-attackers.
/// </summary>
internal static class AttackTaxPaywallBuilder
{
    /// <summary>Ghostly Prison / Propaganda — flat {N} generic per attacker on
    /// the controller only.</summary>
    public static Enchantment BuildFlat(
        string cardName, string manaCost, Player owner, int genericPerAttacker)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var ench = new Enchantment(cardName, manaCost);
        ench.SetOwner(owner);
        ench.SetController(owner);

        var cost = Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(genericPerAttacker);
        var restriction = PayPerAttackerRestriction.FlatMana(
            owner,
            cost,
            isActive: () => ench.Zone == ZoneType.Battlefield);

        AttackRestrictionRegistryProvider.Current.Register(restriction);
        return ench;
    }

    /// <summary>Sphere of Safety — dynamic {X} per attacker (X = the number of
    /// enchantments the controller controls) and also protects the controller's
    /// planeswalkers.</summary>
    public static Enchantment BuildDynamic(
        string cardName,
        string manaCost,
        Player owner,
        Func<Player, int> genericPerAttacker,
        bool protectsPlaneswalkers)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var ench = new Enchantment(cardName, manaCost);
        ench.SetOwner(owner);
        ench.SetController(owner);

        var restriction = PayPerAttackerRestriction.Dynamic(
            owner,
            costPerAttacker: () => Majik.Core.ValueObjects.ManaCost.Zero
                .AddGenericCost(System.Math.Max(0, genericPerAttacker(ench.Controller ?? owner))),
            protectsPlaneswalkers: protectsPlaneswalkers,
            isActive: () => ench.Zone == ZoneType.Battlefield);

        AttackRestrictionRegistryProvider.Current.Register(restriction);
        return ench;
    }
}
