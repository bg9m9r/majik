using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elite Vanguard (M10 / M12, {W}).
///
/// Creature — Human Soldier 2/1. Oracle text: (none — vanilla creature).
///
/// ## Implemented (v1)
///
/// - 2/1 Human Soldier with mana cost {W}, owner / controller stamped.
/// - Vanilla — no abilities, no triggered effects, no static abilities.
///   The only rules interaction is the printed P/T and type line.
///
/// ## Rules references
///
/// - CR 208.1 — vanilla creatures have no abilities.
/// - CR 202.3 — mana value of {W} is 1.
/// - CR 105 — colour is derived from coloured pips; {W} makes this card white.
/// </summary>
[CardName("Elite Vanguard")]
public static class EliteVanguardFactory
{
    public const string CardName = "Elite Vanguard";
    public const string PrintedManaCost = "{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Elite Vanguard owned and controlled by
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
