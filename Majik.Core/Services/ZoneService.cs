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
        // CR 113.5 — mirror the live <see cref="Card.WasCast"/> stamp
        // onto the intent so battlefield-entry replacements (Containment
        // Priest's "if it wasn't cast, exile it instead") can read the
        // cast posture off the in-flight intent without re-fetching the
        // card. SpellCastFlow sets Card.WasCast at stack push time, so
        // the Stack → Battlefield move propagates true; non-cast paths
        // (reanimation, Sneak Attack, Show and Tell, blink, token ETB,
        // Aether Vial) leave Card.WasCast = false and the intent
        // mirrors that.
        var wasCast = (card as Card)?.WasCast ?? false;
        var intent = new ZoneMoveIntent(card, fromZone, toZone, controller, WasCast: wasCast);
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
                 or ZoneType.Graveyard or ZoneType.Exile
                 or ZoneType.Sideboard)
        {
            card.SetController(card.Owner);
        }

        if (card.Owner != null)
        {
            card.Owner.Zones.MoveCard(card, fromZone, finalToZone);
        }

        // CR 400.7 — the card becomes a "new object" on every zone change.
        // For the cast-marker we clear only on actual battlefield exits
        // (Battlefield → anything-else) so that ETB triggers fired off
        // the Stack → Battlefield move and LTB-event subscribers can
        // still read the in-flight stamp earlier in this method. We
        // publish CardMovedEvent FIRST so any LTB subscriber that wants
        // to consult Card.WasCast can do so before the clear runs.
        _eventBus?.Publish(new CardMovedEvent(card, fromZone, finalToZone));

        if (fromZone == ZoneType.Battlefield && finalToZone != ZoneType.Battlefield
            && card is Card concreteForCastClear)
        {
            concreteForCastClear.ClearWasCast();
        }
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
