using Majik.Core.Abilities;
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
    /// The attached <see cref="ReplacementBus"/>, or <c>null</c> when the
    /// service was constructed without one. Exposed so token-creation
    /// helpers (<see cref="Majik.Core.Tokens.TokenFactory"/>) can reach
    /// the same bus the zone service routes ETB intents through without
    /// requiring every caller to thread the bus alongside the
    /// <see cref="ZoneService"/> they already pass.
    /// </summary>
    public ReplacementBus? Replacements => _replacements;

    /// <summary>
    /// Move a card from one zone to another (synchronous path). Routes the
    /// would-move through <see cref="ReplacementBus.Apply{TIntent}"/>, which
    /// drives every applicable replacement on its synchronous path. Prompting
    /// replacements (shock land, Mox Diamond) thus fall back to their
    /// no-context posture here; callers that hold a live
    /// <see cref="Majik.Core.Abilities.ResolutionContext"/> (the async stack-
    /// resolution path) should prefer <see cref="MoveCardAsync"/> so those
    /// replacements <c>await</c> the agent instead of bridging sync-over-async.
    /// </summary>
    public void MoveCard(ICard card, ZoneType fromZone, ZoneType toZone, Player? controller = null)
    {
        var intent = BuildIntent(card, fromZone, toZone, controller);
        if (_replacements != null)
        {
            var replaced = _replacements.Apply(intent);
            if (replaced == null) return; // cancelled
            intent = replaced;
        }

        CommitMove(card, fromZone, intent);
    }

    /// <summary>
    /// PLAN 08 — async twin of <see cref="MoveCard"/>. Pushes the would-move
    /// through <see cref="ReplacementBus.ApplyAsync{TIntent}"/> with the live
    /// <paramref name="ctx"/> so prompting battlefield-entry replacements
    /// (shock-land "pay 2 life", Mox Diamond "discard a land") genuinely
    /// <c>await</c> the controller's agent rather than blocking a thread-pool
    /// thread. Non-prompting replacements behave identically to the sync path.
    /// </summary>
    public async ValueTask MoveCardAsync(
        ICard card, ZoneType fromZone, ZoneType toZone, ResolutionContext ctx,
        Player? controller = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var intent = BuildIntent(card, fromZone, toZone, controller);
        if (_replacements != null)
        {
            var replaced = await _replacements.ApplyAsync(intent, ctx).ConfigureAwait(false);
            if (replaced == null) return; // cancelled
            intent = replaced;
        }

        CommitMove(card, fromZone, intent);
    }

    /// <summary>
    /// CR 614 — validate the transition and build the would-move
    /// <see cref="ZoneMoveIntent"/> that gets pushed through the replacement
    /// bus. Shared by the sync (<see cref="MoveCard"/>) and async
    /// (<see cref="MoveCardAsync"/>) entry points.
    /// CR 113.5 — mirror the live <see cref="Card.WasCast"/> stamp onto the
    /// intent so battlefield-entry replacements (Containment Priest's "if it
    /// wasn't cast, exile it instead") can read the cast posture off the
    /// in-flight intent without re-fetching the card. SpellCastFlow sets
    /// Card.WasCast at stack push time, so the Stack → Battlefield move
    /// propagates true; non-cast paths (reanimation, Sneak Attack, Show and
    /// Tell, blink, token ETB, Aether Vial) leave Card.WasCast = false and
    /// the intent mirrors that.
    /// </summary>
    private ZoneMoveIntent BuildIntent(ICard card, ZoneType fromZone, ZoneType toZone, Player? controller)
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

        var wasCast = (card as Card)?.WasCast ?? false;
        return new ZoneMoveIntent(card, fromZone, toZone, controller, WasCast: wasCast);
    }

    /// <summary>
    /// Apply the post-replacement <paramref name="intent"/> to game state —
    /// the destination zone move, controller stamp, ETB tap / counter
    /// bookkeeping, <see cref="CardMovedEvent"/> publish, and CR 400.7 cast-
    /// sentinel lifecycle. Shared tail of <see cref="MoveCard"/> /
    /// <see cref="MoveCardAsync"/>; identical behaviour regardless of which
    /// bus path produced the intent.
    /// </summary>
    private void CommitMove(ICard card, ZoneType fromZone, ZoneMoveIntent intent)
    {
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
            // CR 400.7 / CR 603.6a — stamp "entered directly from library"
            // sentinel so ETB abilities (Fblthp, the Lost's draw-2 clause)
            // can check whether the card came from the library without a
            // cast (Library → Battlefield, WasCast == false). Only stamped
            // on this specific transition; cast-from-library entries use
            // WasCastFromLibrary instead.
            if (fromZone == ZoneType.Library && card is Card concreteForLibraryPlaced
                && !concreteForLibraryPlaced.WasCast)
            {
                concreteForLibraryPlaced.SetWasPlacedFromLibrary(true);
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
            // CR 400.7 — mirror cast-from-hand sentinel lifecycle. The flag
            // survives Stack → Battlefield (ETB intervening-if reads it)
            // but a subsequent battlefield exit makes the card a new
            // object; a later re-cast / blink / token copy must start
            // from a clean slate. Mirrors WasCast's lifecycle above.
            concreteForCastClear.ClearWasCastFromHand();
            // CR 400.7 — mirror cast-from-library sentinel lifecycle. Same
            // shape as WasCastFromHand: survives Stack → Battlefield but a
            // subsequent battlefield exit resets the object.
            concreteForCastClear.ClearWasCastFromLibrary();
            // CR 400.7 — clear the placed-from-library sentinel on
            // battlefield exit (matching the WasCastFromLibrary lifecycle).
            concreteForCastClear.ClearWasPlacedFromLibrary();
        }
    }

    /// <summary>
    /// Move a card to a zone (automatically determines source zone).
    /// </summary>
    public void MoveCardTo(ICard card, ZoneType toZone, Player? controller = null)
    {
        MoveCard(card, card.Zone, toZone, controller);
    }

    /// <summary>
    /// PLAN 08 — async twin of <see cref="MoveCardTo"/>. Auto-determines the
    /// source zone and routes through <see cref="MoveCardAsync"/> so prompting
    /// battlefield-entry replacements await the agent off <paramref name="ctx"/>.
    /// </summary>
    public ValueTask MoveCardToAsync(
        ICard card, ZoneType toZone, ResolutionContext ctx, Player? controller = null)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        return MoveCardAsync(card, card.Zone, toZone, ctx, controller);
    }

    private static bool IsValidZoneTransition(ZoneType from, ZoneType to) => true;
}
