using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Akroma, Angel of Wrath (Legions, {5}{W}{W}{W}).
///
/// Legendary Creature — Angel 6/6. Oracle text (Scryfall, verified):
///   "Flying, first strike, vigilance, trample, haste
///    Protection from black and from red"
///
/// ## Implemented (v1)
/// - 6/6 Legendary Creature — Angel at {5}{W}{W}{W}, owner / controller
///   wired.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/>("Flying")
///   marker.
/// - <b>First Strike (CR 702.7)</b>: <see cref="KeywordAbility"/>
///   ("First Strike") marker.
/// - <b>Vigilance (CR 702.20)</b>: <see cref="KeywordAbility"/>
///   ("Vigilance") marker.
/// - <b>Trample (CR 702.19)</b>: <see cref="KeywordAbility"/>
///   ("Trample") marker.
/// - <b>Haste (CR 702.10)</b>: <see cref="KeywordAbility"/>("Haste")
///   marker — overrides summoning-sickness per CR 302.6 so Akroma can
///   attack the turn she enters.
/// - <b>Protection from black (CR 702.16)</b>: two-arg
///   <see cref="ProtectionAbility"/>("black") — quality string surface
///   consumed by <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>
///   for DEBT-A enforcement (Damage / Enchant / Block / Target,
///   CR 702.16e).
/// - <b>Protection from red (CR 702.16)</b>: second
///   <see cref="ProtectionAbility"/>("red") — same string-only surface
///   as the black clause. Stacks naturally with the black entry; each
///   colour is its own ability per CR 702.16b.
///
/// ## Notes
/// Vanilla five-keyword + double-protection creature — no activated,
/// triggered, or static effects beyond the printed keyword set. Combat
/// helpers read the keyword markers directly (same wiring as Gisela,
/// Blade of Goldnight / Mantis Rider / every other named-factory
/// creature). Multi-coloured protection ships as two independent
/// <see cref="ProtectionAbility"/> instances rather than a combined
/// "black or red" predicate so the existing colour-string surface in
/// <see cref="Majik.Core.Rules.Protection"/> works unchanged (matches
/// the pattern Sword of Fire and Ice uses for "red" + "blue").
///
/// CR rule references: 205.2 (Legendary), 205.3m (Angel subtype),
/// 702.7 (First strike), 702.9 (Flying), 702.10 (Haste), 702.16
/// (Protection), 702.19 (Trample), 702.20 (Vigilance).
/// </summary>
[CardName("Akroma, Angel of Wrath")]
public static class AkromaAngelOfWrathFactory
{
    public const string CardName = "Akroma, Angel of Wrath";
    public const string PrintedManaCost = "{5}{W}{W}{W}";
    public const int Power = 6;
    public const int Toughness = 6;

    /// <summary>
    /// Construct Akroma, Angel of Wrath. Vanilla keyword + protection
    /// shape — no service wiring required.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Angel });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Evergreen keywords — CR 702.9 Flying, CR 702.7 First Strike,
        // CR 702.20 Vigilance, CR 702.19 Trample, CR 702.10 Haste.
        // KeywordAbility markers read directly by the combat helpers.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("First Strike", card, owner));
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));
        card.AddAbility(new KeywordAbility("Trample", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // CR 702.16 — Protection from black and from red. Each colour is
        // its own ability (CR 702.16b) so we register two independent
        // ProtectionAbility instances. Rules.Protection.HasProtectionFromColor
        // sums across all attached ProtectionAbility markers to enforce
        // DEBT-A (Damage / Enchant / Block / Target — CR 702.16e).
        // ----------------------------------------------------------------
        card.AddAbility(new ProtectionAbility("black"));
        card.AddAbility(new ProtectionAbility("red"));

        return card;
    }
}
