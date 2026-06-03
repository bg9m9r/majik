using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Breaker of Armies (Battle for Zendikar, {8}).
///
/// Creature — Eldrazi 10/8. Oracle text (Scryfall):
///   "All creatures able to block this creature do so."
///
/// ## Implemented (v1)
///
/// - <b>Creature — Eldrazi {8} 10/8</b>. A colourless top-end fatty whose
///   sole ability drags the whole opposing board into combat with it.
/// - <b>"All creatures able to block this creature do so" (CR 509.1c)</b>:
///   represented as a <see cref="KeywordAbility"/> marker
///   (<c>"MustBeBlockedByAllAble"</c>), queried at declare-blockers by
///   <see cref="Majik.Core.Combat.CombatAbilities.MustBeBlockedByAllAble"/>
///   and enforced by the must-block overload of
///   <c>CombatValidator.IsValidBlockDeclaration</c>. Every untapped creature
///   the defending player controls that <em>can legally</em> block this
///   attacker (not tapped, not evasion-exempt, no "can't block" / protection
///   wall) is forced to block it (CR 509.1g). This is a block
///   <em>requirement</em>, the dual of the
///   <c>CantBeBlockedExceptByMinBlockers</c> block <em>restriction</em>.
///
/// CR rule references: 205.3i (Eldrazi subtype), 509.1c (block
/// requirements — "able to block ~ do so"), 509.1g (maximising satisfied
/// requirements).
/// </summary>
[CardName("Breaker of Armies")]
public static class BreakerOfArmiesFactory
{
    public const string CardName = "Breaker of Armies";
    public const string PrintedManaCost = "{8}";
    public const int Power = 10;
    public const int Toughness = 8;

    /// <summary>
    /// Construct Breaker of Armies — a 10/8 colourless Eldrazi carrying the
    /// "all creatures able to block this creature do so" requirement marker
    /// (CR 509.1c).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "All creatures able to block this creature do so" (CR 509.1c).
        // Represented as a KeywordAbility marker with keyword
        // "MustBeBlockedByAllAble". Enforced at block-declaration time by
        // CombatValidator's must-block overload, which forces every
        // defending creature able to legally block this attacker to do so.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(
            "MustBeBlockedByAllAble",
            source: card,
            controller: owner));

        return card;
    }
}
