using Majik.Core.CardData.Definitions;
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
[CardName("Mantis Rider")]
public static class MantisRiderFactory
{
    public const string CardName = "Mantis Rider";
    public const string PrintedManaCost = "{W}{U}{R}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// CardDef DSL — vanilla three-keyword creature. CR 702.9 (Flying),
    /// CR 702.20 (Vigilance), CR 702.10 (Haste) — the markers are read by
    /// the combat-abilities subsystem to gate evasion / tap-on-attack /
    /// summoning-sickness checks.
    /// </summary>
    public static CardDef Define() => CardDef
        .Creature(CardName, PrintedManaCost, Power, Toughness)
        .WithSubtypes(CardSubtype.Human, CardSubtype.Monk)
        .WithKeyword("Flying")
        .WithKeyword("Vigilance")
        .WithKeyword("Haste");

    public static Creature Create(Player owner) =>
        (Creature)CardDefRuntime.Build(Define(), owner);
}
