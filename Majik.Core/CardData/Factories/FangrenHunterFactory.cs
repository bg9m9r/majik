using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fangren Hunter (Darksteel, {3}{G}{G}).
///
/// Creature — Beast 4/4. Oracle text:
///   "Trample"
///
/// ## Implemented (v1)
/// - 4/4 Creature — Beast at {3}{G}{G} (mana value 5), owner/controller
///   stamped.
/// - Trample (CR 702.19) wired as a <see cref="KeywordAbility"/> marker;
///   read by <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> in
///   the combat damage assignment path.
/// </summary>
[CardName("Fangren Hunter")]
public static class FangrenHunterFactory
{
    public const string CardName = "Fangren Hunter";
    public const string PrintedManaCost = "{3}{G}{G}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Fangren Hunter owned and controlled by
    /// <paramref name="owner"/>. No runtime services are required — the
    /// card is a vanilla Trample creature with no triggered or activated
    /// abilities.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample. CombatAbilities.HasTrample reads the marker.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        return card;
    }
}
