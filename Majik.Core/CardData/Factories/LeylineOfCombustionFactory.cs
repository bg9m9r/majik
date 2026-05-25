using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of Combustion (Core Set 2020,
/// {2}{R}{R}).
///
/// Enchantment. Oracle text:
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "Whenever an opponent casts a spell or activates an ability (other
///    than a mana ability) that targets you or a permanent you control,
///    Leyline of Combustion deals 2 damage to that player."
///
/// ## Implemented (v1)
/// - Enchantment shape with mana cost {2}{R}{R}, owner / controller
///   wired.
/// - <b>Opening-hand alt-cost</b> (CR 702.95) — marker
///   <see cref="KeywordAbility"/>
///   (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>)
///   so the shared subscriber picks Combustion up from
///   <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"Whenever an opponent casts a spell or activates an ability
///   that targets you or a permanent you control"</b> (CR 603.6 — when-
///   triggered ability) — needs a targeting-resolution trigger surface
///   keyed off "an opponent's spell / non-mana ability targets you or
///   one of your permanents". The cast/activate triggers exist on the
///   targeting-resolution path but the "targets you or your permanent"
///   predicate isn't yet exposed as a trigger condition. Deferred; the
///   opening-hand half ships standalone today.
/// </summary>
[CardName("Leyline of Combustion")]
public static class LeylineOfCombustionFactory
{
    public const string CardName = "Leyline of Combustion";
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
