using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Services;

/// <summary>
/// Domain service for managing zone operations.
/// Handles card movement between zones with proper validation.
///
/// When a <see cref="ReplacementBus"/> is supplied, every move builds a
/// <see cref="ZoneMoveIntent"/> and pushes it through the bus first;
/// replacements can mutate the destination, force "enters tapped", or
/// cancel the move entirely (CR 614).
/// </summary>
public class ZoneService
{
    private readonly IEventBus? _eventBus;
    private readonly ReplacementBus? _replacements;

    public ZoneService(IEventBus? eventBus = null, ReplacementBus? replacements = null)
    {
        _eventBus = eventBus;
        _replacements = replacements;
    }

    /// <summary>
    /// Move a card from one zone to another.
    /// </summary>
    public void MoveCard(ICard card, ZoneType fromZone, ZoneType toZone, Player? controller = null)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));

        if (card.Zone != fromZone)
        {
            throw new InvalidZoneTransitionException(
                fromZone, toZone,
                $"Card is not in expected zone. Expected: {fromZone}, Actual: {card.Zone}");
        }

        if (!IsValidZoneTransition(fromZone, toZone))
        {
            throw new InvalidZoneTransitionException(fromZone, toZone);
        }

        // CR 614 — funnel intent through replacement bus.
        var intent = new ZoneMoveIntent(card, fromZone, toZone, controller);
        if (_replacements != null)
        {
            var replaced = _replacements.Apply(intent);
            if (replaced == null) return; // cancelled
            intent = replaced;
        }

        var finalToZone = intent.ToZone;
        var finalController = intent.Controller;

        card.SetZone(finalToZone);

        if (finalController != null && finalToZone == ZoneType.Battlefield)
        {
            card.SetController(finalController);
        }

        if (finalToZone == ZoneType.Battlefield && card is Permanent permanent)
        {
            permanent.MarkEnteredBattlefield();
            if (intent.EntersTapped && !permanent.IsTapped)
            {
                permanent.Tap();
            }
            // CR 614.1d — ETB-counter replacement effects accumulated their
            // amount onto the intent; apply now after the permanent has
            // landed so SBAs (Rule 704.5f) see the correct power/toughness.
            if (intent.PlusOneCountersOnEnter > 0)
            {
                permanent.Counters.Add(
                    Majik.Core.Counters.CounterType.PlusOnePlusOne,
                    intent.PlusOneCountersOnEnter);
            }
        }
        else if (finalToZone is ZoneType.Hand or ZoneType.Library
                 or ZoneType.Graveyard or ZoneType.Exile)
        {
            card.SetController(card.Owner);
        }

        if (card.Owner != null)
        {
            card.Owner.Zones.MoveCard(card, fromZone, finalToZone);
        }

        _eventBus?.Publish(new CardMovedEvent(card, fromZone, finalToZone));
    }

    /// <summary>
    /// Move a card to a zone (automatically determines source zone).
    /// </summary>
    public void MoveCardTo(ICard card, ZoneType toZone, Player? controller = null)
    {
        MoveCard(card, card.Zone, toZone, controller);
    }

    private static bool IsValidZoneTransition(ZoneType from, ZoneType to) => true;
}
