using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Services;

/// <summary>
/// Domain service for managing zone operations.
/// Handles card movement between zones with proper validation.
/// </summary>
public class ZoneService
{
    private readonly IEventBus? _eventBus;

    public ZoneService(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// Move a card from one zone to another.
    /// </summary>
    public void MoveCard(ICard card, ZoneType fromZone, ZoneType toZone, Player? controller = null)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        // Validate current zone
        if (card.Zone != fromZone)
        {
            throw new InvalidZoneTransitionException(
                fromZone, 
                toZone, 
                $"Card is not in expected zone. Expected: {fromZone}, Actual: {card.Zone}");
        }

        // Validate zone transition
        if (!IsValidZoneTransition(fromZone, toZone))
        {
            throw new InvalidZoneTransitionException(fromZone, toZone);
        }

        // Update card zone
        card.Zone = toZone;

        // Set controller if provided
        if (controller != null && toZone == ZoneType.Battlefield)
        {
            card.Controller = controller;
        }
        else if (toZone == ZoneType.Hand || toZone == ZoneType.Library || 
                 toZone == ZoneType.Graveyard || toZone == ZoneType.Exile)
        {
            // In these zones, controller is always the owner
            card.Controller = card.Owner;
        }

        // Update zone manager
        if (card.Owner != null)
        {
            card.Owner.Zones.MoveCard(card, fromZone, toZone);
        }

        // Publish domain event
        _eventBus?.Publish(new CardMovedEvent(card, fromZone, toZone));
    }

    /// <summary>
    /// Move a card to a zone (automatically determines source zone).
    /// </summary>
    public void MoveCardTo(ICard card, ZoneType toZone, Player? controller = null)
    {
        MoveCard(card, card.Zone, toZone, controller);
    }

    /// <summary>
    /// Check if a zone transition is valid.
    /// </summary>
    private static bool IsValidZoneTransition(ZoneType from, ZoneType to)
    {
        // Cards can move from any zone to any zone
        // In a full implementation, we'd have more specific rules
        // For example:
        // - Cards can only be cast from hand
        // - Permanents enter battlefield from stack
        // - Cards go to graveyard from battlefield when destroyed
        return true;
    }
}
