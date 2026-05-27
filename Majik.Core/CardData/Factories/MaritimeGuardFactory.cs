using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Maritime Guard (Portal / Portal Second Age /
/// Seventh Edition / various reprints, {1}{U}).
///
/// Creature — Merfolk Soldier 1/3. Oracle text: (none — vanilla creature).
///
/// ## Implemented (v1)
///
/// - 1/3 Merfolk Soldier with mana cost {1}{U}, owner / controller stamped.
/// - Vanilla — no abilities, no triggered effects, no static abilities.
///   The only rules interaction is the printed P/T and type line.
///
/// ## Rules references
///
/// - CR 208.1 — vanilla creatures have no abilities.
/// - CR 202.3 — mana value of {1}{U} is 2.
/// - CR 105 — colour is derived from coloured pips; {U} makes this card blue.
/// </summary>
[CardName("Maritime Guard")]
public static class MaritimeGuardFactory
{
    public const string CardName = "Maritime Guard";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Maritime Guard owned and controlled by
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
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // Vanilla — no abilities to attach (CR 208.1).

        return card;
    }
}
