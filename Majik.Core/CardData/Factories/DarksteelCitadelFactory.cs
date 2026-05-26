using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Darksteel Citadel (Darksteel / reprints).
///
/// Artifact Land. Oracle text:
///   "Indestructible.
///    {T}: Add {C}."
///
/// ## Implemented (v1)
/// - <b>Artifact Land</b> — concrete <see cref="Land"/> with the
///   <see cref="CardType.Artifact"/> additively flagged via
///   <see cref="Card.AddCardType"/> (mirrors Kappa Cannoneer's
///   Artifact-Creature shape). Lands have no mana cost (CR 305.1) —
///   <see cref="Land"/>'s base constructor passes an empty cost string.
/// - <b>Indestructible</b> (CR 702.12) — wired as a
///   <see cref="KeywordAbility"/> marker. Read by
///   <see cref="Majik.Core.CardData.OracleSpellBinder.MoveToGraveyard"/>'s
///   non-creature destroy gate.
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   no stack). Mirrors Wasteland's tap-for-{C} shape.
///
/// ## Notes
/// - Modern legal — Darksteel Citadel is a Modern staple in artifact
///   prison / Affinity-adjacent shells (Mox Opal enabler, Cranial
///   Plating equip target).
/// - Darksteel Forge's "Other artifacts you control have indestructible."
///   anthem covers the Citadel for free via
///   <see cref="Majik.Core.Rules.IndestructibleGrantRegistry"/> — the
///   Citadel's printed Indestructible keyword is the load-bearing one,
///   the anthem is just additive.
/// </summary>
[CardName("Darksteel Citadel")]
public static class DarksteelCitadelFactory
{
    public const string CardName = "Darksteel Citadel";

    /// <summary>
    /// Construct Darksteel Citadel owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);

        // CR 301.1 / 305.1 — Darksteel Citadel is an Artifact Land. The
        // base Land constructor only registers CardType.Land, so additively
        // flag the Artifact type for HasType-based lookups (mirrors Kappa
        // Cannoneer / Esika's Chariot's multi-type shape, and is the gate
        // the Affinity-for-artifacts / Cranial Plating's "creature with
        // the most artifacts" / Mox Opal metalcraft accounting all key on).
        land.AddCardType(CardType.Artifact);

        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Indestructible (CR 702.12). Marker only — destroy gates read
        // KeywordAbility off Permanent.
        // ----------------------------------------------------------------
        land.AddAbility(new KeywordAbility("Indestructible", land, owner));

        // ----------------------------------------------------------------
        // {T}: Add {C}. CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        return land;
    }
}
