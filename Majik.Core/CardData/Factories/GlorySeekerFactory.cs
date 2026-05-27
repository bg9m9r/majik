using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glory Seeker (M14 / M15, {1}{W}).
///
/// Creature — Human Soldier 2/2. Oracle text: (none — vanilla creature).
///
/// ## Implemented (v1)
///
/// - 2/2 Human Soldier with mana cost {1}{W}, owner / controller stamped.
/// - Vanilla — no abilities, no triggered effects, no static abilities.
///   The only rules interaction is the printed P/T and type line.
///
/// ## Rules references
///
/// - CR 208.1 — vanilla creatures have no abilities.
/// - CR 202.3 — mana value of {1}{W} is 2.
/// - CR 105 — colour is derived from coloured pips; {W} makes this card white.
/// </summary>
[CardName("Glory Seeker")]
public static class GlorySeekerFactory
{
    public const string CardName = "Glory Seeker";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Glory Seeker owned and controlled by
    /// <paramref name="owner"/>. The card is vanilla — no abilities are
    /// attached beyond the printed type line and P/T.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // Vanilla — no abilities to attach (CR 208.1).

        return card;
    }
}
