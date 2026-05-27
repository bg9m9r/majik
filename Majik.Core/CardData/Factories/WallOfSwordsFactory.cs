using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wall of Swords (Fifth Edition and many reprints,
/// {3}{W}).
///
/// Creature — Wall 3/5. Oracle text:
///   "Defender.
///    Flying."
///
/// ## Implemented (v1)
/// - <b>Creature — Wall {3}{W} 3/5</b>. Owner + controller wired.
/// - <b>Defender</b> (CR 702.3) — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/>
///   surfaces the can't-attack restriction.
/// - <b>Flying</b> (CR 702.9) — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> and
///   <see cref="Majik.Core.Combat.CombatAbilities.CanBlockFlying"/> surface
///   the evasion and can-block-flyer properties.
///
/// Wall of Swords is a vanilla two-keyword creature — no activated abilities,
/// no triggered abilities, no static effects beyond the two keyword markers.
/// </summary>
[CardName("Wall of Swords")]
public static class WallOfSwordsFactory
{
    public const string CardName = "Wall of Swords";
    public const string PrintedManaCost = "{3}{W}";
    public const int Power = 3;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Wall of Swords. The Defender and Flying keyword markers
    /// are wired unconditionally. No activated abilities, no triggered
    /// abilities.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Wall });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.3 — Defender. KeywordAbility marker so
        // CombatAbilities.HasDefender surfaces the can't-attack rider for
        // BlockLegality / CombatValidator consumers.
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // CR 702.9 — Flying. KeywordAbility marker so
        // CombatAbilities.HasFlying / CanBlockFlying surface evasion
        // enforcement and block-legality checks.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
