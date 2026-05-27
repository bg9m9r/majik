using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boggart Brute (Magic Origins, {2}{R}).
///
/// Creature — Goblin Warrior 3/2. Oracle text:
///   "Menace (This creature can't be blocked except by two or more
///    creatures.)"
///
/// ## Implemented (v1)
/// - 3/2 Creature — Goblin Warrior with mana cost {2}{R}, owner/controller
///   stamped.
/// - <b>Menace (CR 702.110)</b>: <see cref="KeywordAbility"/> marker —
///   <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/> reads it at
///   block-declaration time. Same wiring shape as
///   <see cref="InsolentNeonateFactory"/> / <see cref="GriefFactory"/> /
///   <see cref="HiveOfTheEyeTyrantFactory"/>.
/// </summary>
[CardName("Boggart Brute")]
public static class BoggartBruteFactory
{
    public const string CardName = "Boggart Brute";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Boggart Brute owned and controlled by
    /// <paramref name="owner"/>. The Menace keyword marker is attached
    /// to the card; no additional services are required.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.110 — Menace keyword marker. Consumed by
        // CombatAbilities.HasMenace at block-declaration time.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        return card;
    }
}
