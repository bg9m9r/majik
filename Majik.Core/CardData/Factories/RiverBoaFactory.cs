using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for River Boa (Visions / 8th Edition, {1}{G}).
///
/// Creature — Snake 2/1. Oracle text (Scryfall):
///   "Islandwalk (This creature can't be blocked as long as defending player
///    controls an Island.)
///    {G}: Regenerate this creature."
///
/// ## Implemented
/// - <b>Creature — Snake {1}{G} 2/1</b>.
/// - <b>Islandwalk</b> (CR 702.14) — keyword marker. The blocking restriction
///   ("can't be blocked as long as defending player controls an Island") is
///   the engine's standard landwalk enforcement consulting this keyword string
///   (same posture as Lord of Atlantis' granted Islandwalk).
/// - <b>"{G}: Regenerate this creature."</b> (CR 701.18 / 701.15a) — an
///   <see cref="ActivatedAbility"/> whose sole cost is {G}; on resolve a
///   regeneration shield is created on River Boa via
///   <see cref="Permanent.AddRegenerationShield"/>, consumed by the next
///   destroy this turn (tap, remove from combat, heal damage — CR 701.18).
///   Same shield primitive Mortivore / Lotleth Troll / Skithiryx use. Regular
///   speed; any number of times per turn (shields stack, clear at end of turn).
///
/// CR rule references: 205.3m (Snake subtype), 701.15a / 701.18 (regeneration),
/// 702.14 (Islandwalk).
/// </summary>
[CardName("River Boa")]
public static class RiverBoaFactory
{
    public const string CardName = "River Boa";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 2;
    public const int Toughness = 1;
    public const string RegenerateCost = "{G}";

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Snake });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.14 — Islandwalk. Keyword marker; the can't-be-blocked
        // restriction is enforced by the standard landwalk path that consults
        // this keyword string.
        card.AddAbility(new KeywordAbility("Islandwalk", card, owner));

        // ----------------------------------------------------------------
        // {G}: Regenerate this creature.
        // CR 701.18 — "Regenerate [self]" = create a regeneration shield on
        // River Boa (CR 701.15a), consumed by the next destroy this turn (tap,
        // remove from combat, heal damage). Mirrors Skithiryx / Mortivore.
        // ----------------------------------------------------------------
        var regenerateEffect = new Effect(
            $"{CardName}: regenerate self (CR 701.18)",
            () => card.AddRegenerationShield());

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(RegenerateCost) },
            effects: new IEffect[] { regenerateEffect }));

        return card;
    }
}
