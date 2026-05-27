using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pillarfield Ox (Magic 2011 / Magic 2012, {3}{W}).
///
/// Creature — Ox 2/4. Oracle text: (none — vanilla creature).
///
/// ## Implemented (v1)
///
/// - 2/4 Ox with mana cost {3}{W}, owner / controller stamped.
/// - Vanilla — no abilities, no triggered effects, no static abilities.
///   The only rules interaction is the printed P/T and type line.
///
/// ## Rules references
///
/// - CR 208.1 — vanilla creatures have no abilities.
/// - CR 202.3 — mana value of {3}{W} is 4.
/// - CR 105 — colour is derived from coloured pips; {W} makes this card white.
/// </summary>
[CardName("Pillarfield Ox")]
public static class PillarfieldOxFactory
{
    public const string CardName = "Pillarfield Ox";
    public const string PrintedManaCost = "{3}{W}";
    public const int Power = 2;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Pillarfield Ox owned and controlled by
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
            subtypes: new[] { CardSubtype.Ox });

        card.SetOwner(owner);
        card.SetController(owner);

        // Vanilla — no abilities to attach (CR 208.1).

        return card;
    }
}
