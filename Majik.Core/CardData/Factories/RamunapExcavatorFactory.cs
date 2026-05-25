using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ramunap Excavator (Hour of Devastation).
///
/// Creature — Naga Cleric, {1}{G}{G}, 2/3. Oracle text:
///   "You may play lands from your graveyard."
///
/// ## Implementation
///
/// Ramunap Excavator is the creature analogue of Crucible of Worlds (CR
/// 113.6c / 118.1 — "you may play X from Y" rewrites the zone-restriction
/// half of the land-play action without waiving the usual per-turn
/// land-drop cap from CR 305.2). The implementation mirrors
/// <see cref="CrucibleOfWorldsFactory"/> exactly, swapping only the card
/// shape (Creature — Naga Cleric vs Artifact) and printed mana cost.
/// The runtime permission stamps the same per-card
/// <see cref="Card.MayPlayFromGraveyard"/> flag — multiple Crucible /
/// Ramunap Excavator / Conduit of Worlds permission sources are
/// idempotent (CR 616 / the rules text overlap is benign: the agent's
/// land-play candidate-list surface reads a single bit per land,
/// uncombined).
///
/// This implementation wires:
///
/// - <b>Creature shape</b> (2/3 Naga Cleric {1}{G}{G}, owner / controller).
/// - <b>Static ability marker</b> (<see cref="StaticAbility"/> with the
///   printed description) so shape tests and UI surface "You may play
///   lands from your graveyard." Gated on Ramunap Excavator being on the
///   battlefield via <see cref="StaticAbility.IsActive"/> — CR 113.6,
///   CR 603.6e.
/// - <b>Per-card runtime grant</b>: stamps
///   <see cref="Card.MayPlayFromGraveyard"/> on every Land currently in
///   the controller's graveyard at construction time (snapshot path).
///   The <see cref="Create(Player, IEventBus?)"/> overload additionally
///   subscribes to <see cref="CardMovedEvent"/> so any Land entering the
///   controller's graveyard AFTER construction is also stamped (gated
///   on Ramunap Excavator being on the battlefield, card owner =
///   controller, Land type).
///
/// ## Deferred (v1 gaps)
/// Inherited verbatim from <see cref="CrucibleOfWorldsFactory"/>:
/// - <b>Bot / agent surface</b>: <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>
///   does not yet read <see cref="Card.MayPlayFromGraveyard"/> when
///   building its playable-lands candidate list. Until it does, the
///   flag is structural — the engine accepts the move when a caller
///   issues it, but the heuristic bot won't proactively propose it.
/// - <b>LTB clear</b>: the per-card grants are stamped on entry but not
///   cleared when Ramunap Excavator leaves the battlefield. The flag is
///   benign off-battlefield once the agent layer ALSO checks for a live
///   permission source; re-stamping on a fresh Excavator is idempotent.
/// - <b>Printed land subtype "Naga"</b>: not previously needed; added
///   to <see cref="CardSubtype.Naga"/> alongside this factory.
/// </summary>
[CardName("Ramunap Excavator")]
public static class RamunapExcavatorFactory
{
    public const string CardName = "Ramunap Excavator";
    public const string PrintedManaCost = "{1}{G}{G}";

    /// <summary>Printed static-ability description surfaced on the card.</summary>
    public const string StaticDescription =
        "You may play lands from your graveyard.";

    /// <summary>
    /// Construct Ramunap Excavator with no live event-bus wiring. The
    /// static-ability marker is attached. The per-Land "may play from
    /// graveyard" stamping only runs over the controller's graveyard at
    /// construction time (the snapshot path — suitable for factory-shape /
    /// dispatch tests).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Ramunap Excavator with an optional event bus. When a bus
    /// is supplied, the factory subscribes to <see cref="CardMovedEvent"/>
    /// so any Land entering the controller's graveyard after construction
    /// is also stamped — keeping the grant in sync with the live
    /// graveyard contents while Ramunap Excavator is on the battlefield.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 3,
            subtypes: new[] { CardSubtype.Naga, CardSubtype.Cleric });
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Static-ability marker. CR 113.6e / 603.6e — functions while on
        // the battlefield. Description matches the printed text for shape
        // / UI surfacing. The runtime grant is the per-Land flag below
        // (Excavator's permission semantics aren't expressible through the
        // engine's continuous-effects layer system — Layer 5 / 6 are for
        // characteristics, not for action-legality permissions; same
        // reasoning as Crucible of Worlds).
        // ----------------------------------------------------------------
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: StaticDescription,
            isActiveCheck: () => card.Zone == ZoneType.Battlefield));

        // ----------------------------------------------------------------
        // Per-Land grant. Stamp every Land currently in the controller's
        // graveyard with the "may play from graveyard" flag. When a bus
        // is supplied, also subscribe to CardMovedEvent so any Land that
        // later enters the controller's graveyard is stamped too.
        // ----------------------------------------------------------------
        StampLandsInGraveyard(owner);

        if (eventBus != null)
        {
            var lifecycle = new ExcavatorPermissionLifecycle(card, owner, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Stamp every Land currently in <paramref name="controller"/>'s
    /// graveyard with the "may play from graveyard" runtime flag.
    /// Exposed for tests + the lifecycle binder.
    /// </summary>
    public static void StampLandsInGraveyard(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        foreach (var c in controller.Zones.Graveyard.GetCards().ToList())
        {
            if (c is Card concrete && concrete.HasType(CardType.Land))
            {
                concrete.GrantPlayLandFromGraveyard();
            }
        }
    }

    /// <summary>
    /// Lifecycle binder: subscribes the controller-side CardMovedEvent
    /// stream while Ramunap Excavator is on the battlefield, stamping any
    /// Land that enters the controller's graveyard. v1 doesn't unsubscribe
    /// on LTB (idempotent — re-stamping is a no-op; agent-side gate
    /// will check Excavator's live presence when that integration lands).
    /// </summary>
    private sealed class ExcavatorPermissionLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly IEventBus _eventBus;
        private bool _attached;

        public ExcavatorPermissionLifecycle(
            Creature source, Player controller, IEventBus eventBus)
        {
            _source = source;
            _controller = controller;
            _eventBus = eventBus;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _eventBus.Subscribe<CardMovedEvent>(OnCardMoved);
        }

        private void OnCardMoved(CardMovedEvent e)
        {
            // Only fire while Ramunap Excavator is on the battlefield.
            if (_source.Zone != ZoneType.Battlefield) return;
            // Only stamp lands entering the controller's graveyard.
            if (e.ToZone != ZoneType.Graveyard) return;
            if (e.Card is not Card concrete) return;
            if (!concrete.HasType(CardType.Land)) return;
            if (!ReferenceEquals(concrete.Owner, _controller)) return;

            concrete.GrantPlayLandFromGraveyard();
        }
    }
}
