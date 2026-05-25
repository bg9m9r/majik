using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of the Void's black-side cousin
/// Leyline of Anguish (Modern Horizons 3, {2}{B}{B}).
///
/// Enchantment. Oracle text (per Scryfall, MH3 printing):
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "Creatures you control get +1/+0."
///   "Other creatures get -1/-0."
///
/// ## Implemented (v1)
/// - Enchantment shape with mana cost {2}{B}{B}, owner / controller
///   wired.
/// - <b>Opening-hand alt-cost</b> (CR 702.95) — marker
///   <see cref="KeywordAbility"/>
///   (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>)
///   so the shared subscriber picks Anguish up from
///   <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Power-bump auras</b> ("Creatures you control get +1/+0" / "Other
///   creatures get -1/-0", CR 613) — this is a continuous static on
///   power only. The +1/-1 split needs a controller-partitioned
///   <c>ContinuousEffect</c> (Layer 7c), which the engine has on the
///   creature-bound effect surface but not (yet) as a controller-scoped
///   global aura. Deferred until a global-static layer lands; the
///   opening-hand half ships standalone today.
/// </summary>
[CardName("Leyline of Anguish")]
public static class LeylineOfAnguishFactory
{
    public const string CardName = "Leyline of Anguish";
    public const string PrintedManaCost = "{2}{B}{B}";

    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.95 — Leyline keyword marker.
        card.AddAbility(new KeywordAbility(
            OpeningHandLeylineAlternativeCost.LeylineKeyword, card, owner));

        return card;
    }
}
