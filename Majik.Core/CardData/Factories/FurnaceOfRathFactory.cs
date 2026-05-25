using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Furnace of Rath (Tempest, {2}{R}{R}).
///
/// Enchantment — {2}{R}{R}. Oracle text:
///   "If a source would deal damage to a creature or player, it deals
///    double that damage to that creature or player instead."
///
/// ## Implementation
///
/// - Card identity (Enchantment, mana cost {2}{R}{R}, owner / controller
///   wiring).
/// - <b>Symmetric all-damage doubling</b> (CR 614 / CR 615) — single
///   <see cref="DamageDoubleReplacement"/> registration on the supplied
///   <see cref="ReplacementBus"/> with an always-true predicate (gated
///   only on Furnace of Rath being on the battlefield). No source filter,
///   no target filter, no combat-damage filter — every damage intent the
///   engine routes through the bus while Furnace is on the battlefield
///   gets doubled.
/// - Per-effect dedup in the bus (CR 616.1c) lets the symmetric clause
///   stack: two copies of Furnace of Rath quadruple damage; Furnace +
///   Inquisitor's Flail (combat-only) quadruple equipped-creature
///   combat damage and double everything else, etc.
///
/// ## Notes
/// - Planeswalker-damage targets are doubled too: the v1
///   <see cref="DamageDoubleReplacement"/> primitive doesn't gate on
///   <see cref="DamageIntent.TargetCreature"/> vs
///   <see cref="DamageIntent.TargetPlaneswalker"/>, and Furnace's
///   pre-planeswalker oracle text ("creature or player") is the printed
///   <i>2025-11-14</i> Comp Rules reading of "creature, player, or
///   planeswalker" via CR 605 → CR 119.3 (damage redirected to a
///   planeswalker is still "damage dealt to a player" for replacement
///   purposes pre-MoM; the doubling applies before that redirection).
///   The simpler "double everything on the bus" predicate matches
///   real-world expectations for the family.
/// - Two-overload shape mirrors Inquisitor's Flail / Manabarbs: single-arg
///   <see cref="Create(Player)"/> is shape-only for dispatcher tests (no
///   bus → no replacement registration); the
///   <see cref="Create(Player, ReplacementBus?)"/> overload wires the
///   live doubling clause when a bus is supplied.
/// </summary>
[CardName("Furnace of Rath")]
public static class FurnaceOfRathFactory
{
    public const string CardName = "Furnace of Rath";
    public const string Cost = "{2}{R}{R}";

    /// <summary>
    /// Construct Furnace of Rath with card identity only — no damage-
    /// doubling replacement is registered. Suitable for shape /
    /// dispatcher tests; the bus-driven doubling lives on the
    /// <see cref="Create(Player, ReplacementBus?)"/> overload.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Construct Furnace of Rath. When <paramref name="replacements"/>
    /// is supplied, the symmetric "double every damage intent" CR 614
    /// replacement is registered against it, gated on Furnace being on
    /// the battlefield. Without a bus only the structural shape is
    /// wired.
    /// </summary>
    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Symmetric all-damage doubling (CR 614). No source / target /
        // combat filter — every damage intent on the bus is doubled
        // while Furnace of Rath is on the battlefield.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<DamageIntent>(new DamageDoubleReplacement(
                _ => card.Zone == ZoneType.Battlefield));
        }

        return card;
    }
}
