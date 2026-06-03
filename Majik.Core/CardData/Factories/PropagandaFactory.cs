using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Propaganda (Tempest).
///
/// Enchantment — {2}{U}. Oracle text:
///   "Creatures can't attack you unless their controller pays {2} for each
///    creature they control that's attacking you."
///
/// Mechanically identical to Ghostly Prison — the attack-tax paywall (CR
/// 508.1g, flat {2} per attacker on the controller). See
/// <see cref="AttackTaxPaywallBuilder"/> and <see cref="GhostlyPrisonFactory"/>.
/// </summary>
[CardName("Propaganda")]
public static class PropagandaFactory
{
    public const string CardName = "Propaganda";
    public const string PrintedManaCost = "{2}{U}";

    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return AttackTaxPaywallBuilder.BuildFlat(CardName, PrintedManaCost, owner, genericPerAttacker: 2);
    }
}
