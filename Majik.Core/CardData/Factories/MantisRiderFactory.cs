using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mantis Rider (Khans of Tarkir,
/// Creature — Human Monk {W}{U}{R} 3/3).
///
/// Oracle text:
///   "Flying
///    Vigilance
///    Haste"
///
/// ## Implemented (v1)
/// - 3/3 Creature — Human Monk, mana cost {W}{U}{R}, owner/controller wired.
/// - <b>Flying</b> (CR 702.9) — wired as a <see cref="KeywordAbility"/> marker;
///   read by CombatAbilities.HasFlying and the evasion enforcement path.
/// - <b>Vigilance</b> (CR 702.20) — wired as a <see cref="KeywordAbility"/>
///   marker; read by CombatAbilities.HasVigilance (prevents tapping when
///   declared as an attacker).
/// - <b>Haste</b> (CR 702.10) — wired as a <see cref="KeywordAbility"/>
///   marker; read by CombatAbilities.HasHaste (overrides summoning-sickness
///   check per Rule 302.6).
///
/// ## Notes
/// This is a vanilla three-keyword creature — no activated abilities,
/// triggered abilities, or static effects. Multiple copies stack correctly
/// (each instance carries its own keyword set; HasFlying/HasVigilance/HasHaste
/// are idempotent over multiple checks).
/// </summary>
public static class MantisRiderFactory
{
    public const string CardName = "Mantis Rider";
    public const string PrintedManaCost = "{W}{U}{R}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Mantis Rider owned and controlled by <paramref name="owner"/>.
    /// Flying, Vigilance, and Haste keyword markers are always wired.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Monk });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. KeywordAbility marker; CombatAbilities.HasFlying
        // reads it for evasion enforcement in the declare-attackers step.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.20 — Vigilance. KeywordAbility marker; CombatAbilities.HasVigilance
        // suppresses the tap-when-attacking rule (Rule 506.3a) in declare-attackers.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // CR 702.10 — Haste. KeywordAbility marker; CombatAbilities.HasHaste
        // overrides the summoning-sickness guard (Rule 302.6 / 706.10).
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        return card;
    }
}
