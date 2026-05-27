using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Child of Night (Magic 2011, {1}{B}).
///
/// Creature — Vampire 2/1. Oracle text:
///   "Lifelink"
///
/// ## Implementation
///
/// - 2/1 Creature — Vampire at printed cost {1}{B}.
/// - <b>Lifelink (CR 702.15)</b>: <see cref="KeywordAbility"/> marker —
///   combat helpers in <see cref="Majik.Core.Combat.CombatAbilities"/>
///   read it directly to apply life gain equal to damage dealt.
/// </summary>
[CardName("Child of Night")]
public static class ChildOfNightFactory
{
    public const string CardName = "Child of Night";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 2;
    public const int Toughness = 1;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.15 — Lifelink marker. Combat helpers in CombatAbilities
        // read this directly; same shape as Vault Skirge's Lifelink wiring.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        return card;
    }
}
