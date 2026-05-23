using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Rules;

/// <summary>
/// CR 117.1 — when a player has priority, the actions available depend
/// on the card type and any speed-modifying keywords. Sorceries / creatures
/// / enchantments / artifacts may only be cast at sorcery speed (active
/// player's main phase, empty stack); instants and any card with Flash
/// (CR 702.8) can be cast at instant speed.
///
/// External flash grants — e.g. Sigarda's Aid: "Equipment and Auras you
/// control have flash." — are consulted via <see cref="FlashGrantRegistry"/>
/// so the grant applies even while the card is in hand (i.e. at the moment
/// of the sorcery-speed gate).
/// </summary>
public static class TimingRules
{
    public static bool CanCastAtInstantSpeed(ICard card)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (card.HasType(CardType.Instant)) return true;
        if (card.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        // CR 702.8 — flash granted by an external object (Sigarda's Aid).
        return FlashGrantRegistry.HasGrantedFlash(card);
    }
}
