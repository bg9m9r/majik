using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wall of Wood (Alpha and many reprints).
///
/// Creature — Wall, mana cost {G}, 0/3.
/// Oracle text: "Defender."
///
/// Wall of Wood is a vanilla green wall — the only ability is the
/// Defender keyword (CR 702.3). No activated abilities, no triggered
/// abilities, no other keywords.
///
/// ## Implemented
/// - Card identity: 0/3 Creature — Wall, mana cost {G} (MV 1).
/// - <b>Defender</b> (CR 702.3) — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/>
///   surfaces the can't-attack restriction.
///
/// ## Deferred
/// - None. Wall of Wood is a fully vanilla card; the Defender keyword
///   marker is the complete oracle implementation.
/// </summary>
[CardName("Wall of Wood")]
public static class WallOfWoodFactory
{
    public const string CardName = "Wall of Wood";
    public const string PrintedManaCost = "{G}";
    public const int Power = 0;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Wall of Wood for the given <paramref name="owner"/>.
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

        // CR 702.3 — Defender keyword marker. Wired so
        // CombatAbilities.HasDefender surfaces it for block-legality
        // (BlockLegality.cs reads this via the KeywordAbility-marker
        // fallback path). Wall of Wood has no other abilities.
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        return card;
    }
}
