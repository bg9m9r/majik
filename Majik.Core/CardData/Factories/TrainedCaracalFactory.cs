using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Trained Caracal (Ixalan / M19 reprint, {W}).
///
/// Creature — Cat 1/1. Oracle text:
///   "Lifelink"
///
/// ## Implementation (v1)
///
/// - 1/1 Creature — Cat at {W}; owner and controller set.
/// - <b>Lifelink (CR 702.15)</b>: <see cref="KeywordAbility"/> marker
///   consumed by the standard combat-damage life-gain pipeline in
///   <see cref="Majik.Core.Combat.CombatAbilities"/>. Same wiring as
///   Ocelot Pride and Vault Skirge.
/// </summary>
[CardName("Trained Caracal")]
public static class TrainedCaracalFactory
{
    public const string CardName = "Trained Caracal";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 1;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cat });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.15 — Lifelink. KeywordAbility marker consumed by the
        // standard combat-damage life-gain pipeline.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        return card;
    }
}
