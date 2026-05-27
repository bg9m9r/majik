using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ancient Brontodon (Ixalan, {6}{G}{G}).
///
/// Creature — Dinosaur 9/9. Oracle text: (none — vanilla).
///
/// ## Implementation
/// - 9/9 Dinosaur, mana cost {6}{G}{G} (mana value 8, CR 202.3).
/// - No abilities (vanilla). CR 205.3m — Dinosaur is a valid creature
///   subtype.
/// </summary>
[CardName("Ancient Brontodon")]
public static class AncientBrontodonFactory
{
    public const string CardName = "Ancient Brontodon";
    public const string PrintedManaCost = "{6}{G}{G}";
    public const int Power = 9;
    public const int Toughness = 9;

    /// <summary>
    /// Construct Ancient Brontodon owned and controlled by
    /// <paramref name="owner"/>. No abilities are attached (vanilla).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Dinosaur });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
