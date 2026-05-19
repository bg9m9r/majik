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
/// </summary>
public static class TimingRules
{
    public static bool CanCastAtInstantSpeed(ICard card)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (card.HasType(CardType.Instant)) return true;
        return card.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase));
    }
}
