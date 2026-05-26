using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glistener Elf (New Phyrexia, {G}).
///
/// Creature — Phyrexian Elf Warrior 1/1. Oracle text:
///   "Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)"
///
/// ## Implemented (v1)
/// - 1/1 Creature — Phyrexian Elf Warrior, mana cost {G}, owner / controller
///   wired.
/// - Infect (CR 702.90) — attached as a <see cref="KeywordAbility"/> marker.
///   The damage-replacement primitive (poison to players, -1/-1 counters to
///   creatures) is engine-side; this factory contributes a structurally
///   correct marker so combat / damage code can consult it once the
///   replacement lands.
///
/// ## Notes
/// - The Phyrexian subtype on a Glistener Elf is a creature subtype
///   (CR 205.3m), not the Phyrexian mana symbol. Glistener Elf has no
///   Phyrexian-mana cost.
/// </summary>
[CardName("Glistener Elf")]
public static class GlistenerElfFactory
{
    public const string CardName = "Glistener Elf";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Glistener Elf owned and controlled by
    /// <paramref name="owner"/>. The Infect keyword marker is attached.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[]
            {
                CardSubtype.Phyrexian,
                CardSubtype.Elf,
                CardSubtype.Warrior,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // Infect (CR 702.90) — keyword marker. The damage-replacement
        // primitive (poison counters on players, -1/-1 counters on creatures)
        // is engine-side; this factory exposes the marker so combat code can
        // consult it once the replacement lands.
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        return card;
    }
}
