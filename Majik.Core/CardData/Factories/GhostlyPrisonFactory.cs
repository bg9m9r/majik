using System.Linq;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghostly Prison (Champions of Kamigawa).
///
/// Enchantment — {2}{W}. Oracle text:
///   "Creatures can't attack you unless their controller pays {2} for each
///    creature they control that's attacking you."
///
/// This is the canonical attack-tax paywall (CR 508.1g). On entering the
/// battlefield it registers a <see cref="PayPerAttackerRestriction.FlatMana"/>
/// (flat {2} per attacker on the controller) onto the per-game
/// <see cref="AttackRestrictionRegistryProvider"/>. <see cref="CombatFlow"/>
/// consults that registry right after attackers are declared and charges the
/// {2} per attacker attacking the protected player; an attacker whose
/// controller can't or won't pay is un-declared (CR 508.1g). The restriction
/// is gated on the enchantment still being on the battlefield, so it
/// auto-deactivates when Ghostly Prison leaves (no LTB unregister needed —
/// mirrors Static Prison's zone-guarded replacement).
/// </summary>
[CardName("Ghostly Prison")]
public static class GhostlyPrisonFactory
{
    public const string CardName = "Ghostly Prison";
    public const string PrintedManaCost = "{2}{W}";

    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return AttackTaxPaywallBuilder.BuildFlat(CardName, PrintedManaCost, owner, genericPerAttacker: 2);
    }
}
