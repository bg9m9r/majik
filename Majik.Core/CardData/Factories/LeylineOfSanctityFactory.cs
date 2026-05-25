using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of Sanctity (Magic 2011, {2}{W}{W}).
///
/// Enchantment. Oracle text:
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "You have hexproof. (You can't be the target of spells or abilities
///    your opponents control.)"
///
/// ## Implemented (v1)
/// - Enchantment shape with mana cost {2}{W}{W}, owner / controller
///   wired.
/// - <b>Opening-hand alt-cost</b> (CR 702.95) — marker
///   <see cref="KeywordAbility"/>
///   (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>)
///   so the shared subscriber picks Sanctity up from
///   <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"You have hexproof"</b> — player-level hexproof (CR 702.11)
///   requires a player-keyword infrastructure that doesn't exist in the
///   engine today (creatures-only via <c>CreatureCharacteristics</c>).
///   <see cref="Majik.Core.CardData.Factories.VeilOfSummerFactory"/>
///   already documents the same gap on its "you and permanents you
///   control gain hexproof" rider. A future player-protection layer
///   should wire Sanctity's static into the same surface; for now the
///   card ships as a vanilla enchantment on the battlefield with the
///   opening-hand alt-cost lit up.
/// </summary>
[CardName("Leyline of Sanctity")]
public static class LeylineOfSanctityFactory
{
    public const string CardName = "Leyline of Sanctity";
    public const string PrintedManaCost = "{2}{W}{W}";

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

        // CR 702.95 — Leyline keyword marker. The shared
        // OpeningHandLeylineAlternativeCost subscriber scans hands for
        // this keyword on OpeningHandCheckEvent and prompts the agent.
        card.AddAbility(new KeywordAbility(
            OpeningHandLeylineAlternativeCost.LeylineKeyword, card, owner));

        return card;
    }
}
