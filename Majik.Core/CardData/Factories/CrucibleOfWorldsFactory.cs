using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crucible of Worlds (Fifth Dawn, {3}).
///
/// Artifact. Oracle text:
///   "You may play land cards from your graveyard."
///
/// ## Implementation
///
/// Crucible's clause is a permission-grant static ability (CR 113.6c /
/// 118.1 — "you may play X from Y" rewrites the zone-restriction half
/// of the land-play action without waiving the usual per-turn land-drop
/// cap from CR 305.2). This implementation wires:
///
/// - <b>Artifact shape</b> ({3}, owner / controller).
/// - <b>Static ability marker</b> (<see cref="StaticAbility"/> with the
///   printed description) so shape tests and UI surface "You may play
///   land cards from your graveyard." It is gated on Crucible being on
///   the battlefield via <see cref="StaticAbility.IsActive"/> — CR 113.6,
///   CR 603.6e (static abilities function while their source is on the
///   battlefield).
/// - <b>Per-card runtime grant</b>: an
///   <see cref="ICrucibleOfWorldsGrant"/> on every Land card currently
///   in the controller's graveyard stamps a bit flag the engine /
///   bot / agent layer can read to know that land is a legal target for
///   <see cref="Majik.Core.Players.Agents.PriorityAction.PlayLand"/>.
///   See <see cref="Card.GrantPlayLandFromGraveyard"/>.
///
/// ## Why a per-card grant, not a per-player grant
///
/// The engine already exposes <see cref="Card.RuntimeGraveyardCastCost"/>
/// + <see cref="Costs.GraveyardCastAlternativeCost"/> for "you may CAST X
/// from your graveyard" (Yawgmoth's Will, Lurrus). Lands aren't cast —
/// they're played via <see cref="Majik.Core.Players.Agents.PriorityAction.PlayLand"/>
/// directly through <see cref="Majik.Core.Game.PriorityLoop"/>, which
/// already routes the card through <see cref="Majik.Core.Services.ZoneService.MoveCardTo"/>
/// without checking source zone (it only enforces the
/// <see cref="Majik.Core.Game.LandDropTracker"/> per-turn cap + phase +
/// stack-empty + active-player gate). So the engine-side land-play path
/// already supports playing lands from any zone the agent proposes.
///
/// The remaining piece is exposing the option to the agent: a per-card
/// <see cref="Card.MayPlayFromGraveyard"/> flag that the
/// <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/> + future UI
/// can read to surface graveyard-lands in the playable-land candidate
/// list. The bus-aware overload stamps the flag on every Land currently
/// in the controller's graveyard at ETB time and on every Land that
/// subsequently enters the graveyard (via <see cref="CardMovedEvent"/>
/// subscription).
///
/// ## Deferred (v1 gaps)
/// - <b>Bot / agent surface</b>: <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>
///   does not yet read <see cref="Card.MayPlayFromGraveyard"/> when
///   building its playable-lands candidate list. Until it does, the
///   flag is structural — the engine accepts the move when a caller
///   issues it, but the heuristic bot won't proactively propose it.
///   This is the same posture as Yawgmoth's Will's grave-cast surface
///   on first ship.
/// - <b>LTB clear</b>: the per-card grants are stamped on entry but not
///   cleared when Crucible leaves the battlefield. The flag is benign
///   off-battlefield (the agent surface that reads it ALSO checks
///   "is there an active Crucible permission for this player" — once
///   that integration lands), and re-stamping on a fresh Crucible is
///   idempotent.
/// - <b>Multiple Crucibles / off-Crucible "play land from graveyard"
///   effects</b> (Ramunap Excavator, Conduit of Worlds) share the same
///   flag — first one to stamp wins; subsequent ones are idempotent.
/// </summary>
[CardName("Crucible of Worlds")]
public static class CrucibleOfWorldsFactory
{
    public const string CardName = "Crucible of Worlds";
    public const string PrintedManaCost = "{3}";

    /// <summary>Printed static-ability description surfaced on the card.</summary>
    public const string StaticDescription =
        "You may play land cards from your graveyard.";

    /// <summary>
    /// Construct Crucible of Worlds with no live event-bus wiring. The
    /// static-ability marker is attached. The per-Land "may play from
    /// graveyard" stamping only runs over the controller's graveyard at
    /// construction time (the snapshot path — suitable for factory-shape /
    /// dispatch tests).
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Crucible of Worlds with an optional event bus. When a
    /// bus is supplied, the factory subscribes to <see cref="CardMovedEvent"/>
    /// so any Land entering the controller's graveyard after construction
    /// is also stamped — keeping the grant in sync with the live
    /// graveyard contents while Crucible is on the battlefield.
    /// </summary>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Static-ability marker. CR 113.6e / 603.6e — functions while on
        // the battlefield. Description matches the printed text for shape
        // / UI surfacing. The runtime grant is the per-Land flag below
        // (Crucible's permission semantics aren't expressible through the
        // engine's continuous-effects layer system — Layer 5 / 6 are for
        // characteristics, not for action-legality permissions).
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
            var lifecycle = new CruciblePermissionLifecycle(card, owner, eventBus);
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
    /// stream while Crucible is on the battlefield, stamping any Land
    /// that enters the controller's graveyard. v1 doesn't unsubscribe
    /// on LTB (idempotent — re-stamping is a no-op; agent-side gate
    /// will check Crucible's live presence when that integration lands).
    /// </summary>
    private sealed class CruciblePermissionLifecycle
    {
        private readonly Artifact _source;
        private readonly Player _controller;
        private readonly IEventBus _eventBus;
        private bool _attached;

        public CruciblePermissionLifecycle(
            Artifact source, Player controller, IEventBus eventBus)
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
            // Only fire while Crucible is on the battlefield.
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
