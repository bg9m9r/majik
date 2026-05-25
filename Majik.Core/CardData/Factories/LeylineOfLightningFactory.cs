using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of Lightning (Modern Horizons 3,
/// {2}{R}{R}).
///
/// Enchantment. Oracle text (per Scryfall, MH3 printing):
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "Whenever you cast your first spell each turn, Leyline of Lightning
///    deals 1 damage to any target."
///
/// ## Implemented (v1)
/// - Enchantment shape with mana cost {2}{R}{R}, owner / controller
///   wired.
/// - <b>Opening-hand alt-cost</b> (CR 702.95) — marker
///   <see cref="KeywordAbility"/>
///   (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>)
///   so the shared subscriber picks Lightning up from
///   <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"Whenever you cast your first spell each turn..."</b> (CR 603.3,
///   first-spell-per-turn trigger condition) — needs a per-turn "spells
///   cast" counter on the controller plus a targeted 1-damage triggered
///   ability. The trigger surface exists
///   (<c>Triggers.OnSpellCastByController</c>), but a "first-only" gate
///   keyed off <c>TurnState</c> doesn't yet — deferred. The opening-
///   hand half ships standalone today.
/// </summary>
[CardName("Leyline of Lightning")]
public static class LeylineOfLightningFactory
{
    public const string CardName = "Leyline of Lightning";
    public const string PrintedManaCost = "{2}{R}{R}";

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
