using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Plague Stinger (Scars of Mirrodin, {1}{B}).
///
/// Creature — Phyrexian Insect 1/1. Oracle text:
///   "Flying.
///    Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)"
///
/// ## Implemented (v1)
/// - 1/1 Creature — Phyrexian Insect, mana cost {1}{B}, owner / controller
///   wired.
/// - Flying (CR 702.9) — <see cref="KeywordAbility"/> marker; combat reads
///   this directly.
/// - Infect (CR 702.90) — <see cref="KeywordAbility"/> marker. The damage
///   replacement (poison counters / -1/-1 counters) is engine-side; this
///   factory exposes a structurally correct marker.
/// </summary>
[CardName("Plague Stinger")]
public static class PlagueStingerFactory
{
    public const string CardName = "Plague Stinger";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Plague Stinger owned and controlled by
    /// <paramref name="owner"/>. Flying + Infect keyword markers are attached.
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
                CardSubtype.Insect,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // Flying (CR 702.9) — combat reads this marker directly.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // Infect (CR 702.90) — keyword marker. See class xmldoc.
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        return card;
    }
}
