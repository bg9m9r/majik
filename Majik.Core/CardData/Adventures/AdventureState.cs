using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Adventures;

/// <summary>
/// CR 715 — Adventure cards. The card has two halves: a creature face
/// and an Adventure (instant/sorcery) face. Casting as Adventure exiles
/// the card; the controller may then cast the creature face from exile
/// later (CR 715.4). After casting from exile, the card returns to its
/// "main" zone (graveyard or battlefield) normally.
///
/// MVP state: tracks whether the card is in adventure-exile and whether
/// the creature face has been cast.
/// </summary>
public sealed class AdventureState
{
    public ICard Card { get; }
    public bool InAdventureExile { get; private set; }
    public bool CreatureFaceCast { get; private set; }

    public AdventureState(ICard card)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
    }

    /// <summary>Cast as adventure: exile the card.</summary>
    public void CastAsAdventure(Player controller)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        controller.Zones.Hand.RemoveCard(Card);
        controller.Zones.Exile.AddCard(Card);
        Card.SetZone(ZoneType.Exile);
        InAdventureExile = true;
    }

    /// <summary>Cast creature face from exile (CR 715.4) — only legal while
    /// the card is in adventure-exile.</summary>
    public bool CastCreatureFromExile(Player controller)
    {
        if (!InAdventureExile) return false;
        controller.Zones.Exile.RemoveCard(Card);
        controller.Zones.Hand.AddCard(Card);
        Card.SetZone(ZoneType.Hand);
        InAdventureExile = false;
        CreatureFaceCast = true;
        return true;
    }
}
