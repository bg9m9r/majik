using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fugitive Wizard (Portal / Portal Second Age, {U}).
///
/// Creature — Human Wizard 1/1. Oracle text: (none — vanilla creature).
///
/// ## Implemented (v1)
///
/// - 1/1 Human Wizard with mana cost {U}, owner / controller stamped.
/// - Vanilla — no abilities, no triggered effects, no static abilities.
///   The only rules interaction is the printed P/T and type line.
///
/// ## Rules references
///
/// - CR 208.1 — vanilla creatures have no abilities.
/// - CR 202.3 — mana value of {U} is 1.
/// - CR 105.2 — colour is derived from coloured pips; {U} makes this card Blue.
/// </summary>
[CardName("Fugitive Wizard")]
public static class FugitiveWizardFactory
{
    public const string CardName = "Fugitive Wizard";
    public const string PrintedManaCost = "{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Fugitive Wizard owned and controlled by
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // Vanilla — no abilities to attach (CR 208.1).

        return card;
    }
}
