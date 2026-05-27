using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Charity Extractor ({3}{B}).
///
/// Creature — Human Knight 1/5. Oracle text:
///   "Lifelink"
///
/// A defensive black creature with lifelink — Charity Extractor is a
/// 1/5 wall-like body that gains life whenever it deals damage (CR 702.15).
/// Pairs a Human Knight creature type with a durable toughness-five frame.
/// Charity Extractor is purely a vanilla lifelinker: no triggers, no
/// activated abilities, just the printed Lifelink keyword.
///
/// ## Implementation
///
/// - 1/5 <see cref="Creature"/> with <see cref="CardSubtype.Human"/> and
///   <see cref="CardSubtype.Knight"/>, mana cost {3}{B} (mana value 4,
///   black — CR 202.3 / CR 105.1).
/// - <b>Lifelink (CR 702.15)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The damage-with-lifelink path reads the marker directly.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Charity Extractor")]
public static class CharityExtractorFactory
{
    public const string CardName = "Charity Extractor";
    public const string PrintedManaCost = "{3}{B}";
    public const int Power = 1;
    public const int Toughness = 5;

    /// <summary>
    /// Constructs Charity Extractor — a {3}{B} 1/5 Creature — Human Knight
    /// with the Lifelink keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.15 — Lifelink marker. Damage-with-lifelink path reads marker.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        return card;
    }
}
