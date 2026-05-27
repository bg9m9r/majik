using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skyknight Legionnaire (Ravnica: City of Guilds,
/// {1}{R}{W} Creature — Human Knight 2/2).
///
/// Oracle text:
///   "Flying
///    Haste"
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Knight, mana cost {1}{R}{W} (mana value 3),
///   owner/controller wired.
/// - <b>Flying</b> (CR 702.9) — wired as a <see cref="KeywordAbility"/> marker;
///   read by CombatAbilities.HasFlying and the evasion enforcement path.
/// - <b>Haste</b> (CR 702.10) — wired as a <see cref="KeywordAbility"/>
///   marker; read by CombatAbilities.HasHaste (overrides summoning-sickness
///   check per Rule 302.6).
///
/// ## Notes
/// Skyknight Legionnaire is a vanilla two-keyword creature — no activated
/// abilities, triggered abilities, or static effects. Colours (Red + White)
/// are derived from the printed mana cost at load time by CardColors.GetColors
/// (CR 202.2). The two-pip cost ({1}{R}{W}) yields a mana value of 3.
///
/// CR rule references: 202.2 (colour from mana cost), 205.3m (Human / Knight
/// subtypes), 302.6 (summoning sickness / Haste override), 702.9 (Flying),
/// 702.10 (Haste).
/// </summary>
[CardName("Skyknight Legionnaire")]
public static class SkyknightLegionnaireFactory
{
    public const string CardName = "Skyknight Legionnaire";
    public const string PrintedManaCost = "{1}{R}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// CardDef DSL — vanilla two-keyword creature. CR 702.9 (Flying),
    /// CR 702.10 (Haste) — the markers are read by the combat-abilities
    /// subsystem to gate evasion / summoning-sickness checks.
    /// </summary>
    public static CardDef Define() => CardDef
        .Creature(CardName, PrintedManaCost, Power, Toughness)
        .WithSubtypes(CardSubtype.Human, CardSubtype.Knight)
        .WithKeyword("Flying")
        .WithKeyword("Haste");

    public static Creature Create(Player owner) =>
        (Creature)CardDefRuntime.Build(Define(), owner);
}
