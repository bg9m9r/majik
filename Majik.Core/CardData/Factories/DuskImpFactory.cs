using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dusk Imp (Portal, {2}{B}).
///
/// Creature — Imp 2/1. Oracle text:
///   "Flying"
///
/// ## Implemented (v1)
/// - 2/1 Creature — Imp, mana cost {2}{B}.
/// - <b>Flying</b> (CR 702.9) as a <see cref="KeywordAbility"/> marker;
///   read by CombatAbilities.HasFlying and the evasion enforcement path.
///
/// ## Posture
/// Single-arg <see cref="Create(Player)"/> path. Vanilla body with a
/// Flying marker — no triggered abilities, no activated abilities beyond
/// the intrinsic ones.
/// </summary>
[CardName("Dusk Imp")]
public static class DuskImpFactory
{
    public const string CardName = "Dusk Imp";
    public const string PrintedManaCost = "{2}{B}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Dusk Imp with card identity and Flying keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Imp });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Marker keyword; combat code reads it.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
