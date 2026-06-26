using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Jace, the Mind Sculptor (Worldwake, {2}{U}{U}).
///
/// Legendary Planeswalker — Jace, starting loyalty 3.
/// Oracle text (Scryfall, verified):
///   "+2: Look at the top card of target player's library. You may put
///         that card on the bottom of that player's library.
///    0: Draw three cards, then put two cards from your hand on top of
///         your library in any order.
///    −1: Return target creature to its owner's hand.
///    −12: Exile all cards from target player's library, then that
///         player shuffles their hand into their library."
///
/// ## Implemented (v1)
/// - Legendary Planeswalker — Jace at {2}{U}{U}, starting loyalty 3
///   (CR 306.1 / CR 205.3j — Jace planeswalker subtype).
/// - <b>+2: peek top → may bottom (CR 606 + CR 701.20)</b>: when
///   <paramref name="targetPlayerResolver"/> is non-null, peeks the top
///   card of the resolved target's library and, by v1 deterministic
///   choice, sends it to the bottom (the "you may" is auto-accepted;
///   agent-driven choice is the same gap Wrenn / Karn have). No-resolver
///   path: legal no-op tail (loyalty change still applies).
///   <b>No reveal event by design</b>: "Look at the top card" is a private
///   peek (CR 701.15) — only the activating player sees it — so it
///   deliberately does NOT publish a <see cref="Majik.Core.Events.CardRevealedEvent"/>.
///   This is distinct from the "you may <i>reveal</i>" loyalty abilities
///   (Tezzeret +1, Narset -2) which DO make the chosen card public (CR 701.16).
/// - <b>0: draw 3, then bottom-up-top 2 from hand (CR 606 + CR 121 +
///   CR 701.20)</b>: draws via <see cref="Fx.DrawCards"/>; then takes
///   the first two cards in hand and re-inserts them at index 0 of
///   controller's library (top), preserving printed-order semantics
///   (CR 608.2c). Agent-picked order is the deferred gap. With &lt; 2
///   cards in hand the loop short-circuits cleanly.
/// - <b>-1: Return target creature to owner's hand (CR 606 + CR
///   701.20)</b>: when <paramref name="targetCreatureResolver"/> is
///   non-null, routes the first resolved creature through
///   <see cref="Fx.BounceToHand"/>. No resolver = legal no-op tail.
/// - <b>-12: Exile target player's library, then they shuffle hand
///   into library (CR 606 + CR 701.20 + CR 701.20a)</b>: when
///   <paramref name="targetPlayerResolver"/> is non-null, bulk-moves
///   every card from the target's library to their exile zone via
///   raw zone manipulation; then bulk-moves every card from their hand
///   to their library; then shuffles via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (which routes through
///   the registered <see cref="Majik.Core.Random.GameRandom"/> for
///   deterministic replay and publishes a
///   <see cref="Majik.Core.Events.LibraryShuffledEvent"/>).
///
/// ## Implemented (v1) — loyalty target prompts
/// - <b>+2 / -1 / -12 declare real <see cref="TargetRequest"/>s</b>
///   (CR 602.2b): each targeted loyalty ability declares a TargetRequest
///   with a live <c>CandidateGatherer</c> so the loyalty dispatch path
///   (<c>TurnDriver.DispatchLoyalty</c> → <c>CandidateGatherer</c> →
///   <c>agent.ChooseTargetsAsync</c> → <c>SetChosenTargets</c>) prompts the
///   activating player's agent. Each effect body reads the CHOSEN target off
///   the <see cref="ResolutionContext"/> (<c>rc.ChosenTargets[0][0]</c>) with
///   a CR 608.2b legality re-check, falling back to the captured resolver
///   only on the legacy direct-activation path (the captured resolver was
///   null on the routed prod build — the resolver-null bug class). +2 / -12
///   target a player (any player — "target player"); -1 targets any
///   battlefield creature.
///
/// ## Implemented (v1) — movement provenance
/// - <b>ZoneService routing on -12 bulk moves + +2 bottom hop</b>: the -12's
///   per-card library→exile and hand→library hops and the +2's Library→Library
///   bottom hop now route through the registered
///   <see cref="Majik.Core.Services.ZoneService"/>
///   (<see cref="Majik.Core.Services.ZoneServiceRegistry.Get(Player)"/>) so a
///   <see cref="Majik.Core.Events.CardMovedEvent"/> publishes for each hop
///   (CR 400.7 — each zone change is a distinct move). Raw-zone fallback when no
///   service is registered (shape / dispatcher-test paths) preserves the exact
///   prior end state without events.
///
/// ## Deferred (v1 gaps)
/// - <b>0's "two cards from your hand"</b>: still uses first-in-hand. The 0
///   is a NON-targeted loyalty ability (it asks the controller to choose
///   cards in hand, not a target), so agent-driven hand-card choice is a
///   separate gap from the target-prompt wiring.
/// </summary>
[CardName("Jace, the Mind Sculptor")]
public static class JaceTheMindSculptorFactory
{
    public const string CardName = "Jace, the Mind Sculptor";
    public const string Cost = "{2}{U}{U}";
    public const int StartingLoyalty = 3;
    public const int ZeroDrawCount = 3;
    public const int ZeroTopReturnCount = 2;

    /// <summary>
    /// Construct Jace with no resolvers wired — +2 / -1 / -12 clauses
    /// no-op; 0 still runs (draw + hand re-order are controller-scoped).
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, targetPlayerResolver: null, targetCreatureResolver: null);

    /// <summary>
    /// Construct Jace, the Mind Sculptor.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="targetPlayerResolver">Returns target-player candidates
    /// for +2 / -12 at activation time. v1 picks the first. May be null
    /// — those clauses no-op.</param>
    /// <param name="targetCreatureResolver">Returns target-creature
    /// candidates for -1 at activation time. v1 picks the first. May be
    /// null — the -1 clause no-ops.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? targetPlayerResolver,
        Func<IReadOnlyList<Creature>>? targetCreatureResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var jace = new Planeswalker(
            name: CardName,
            manaCost: Cost,
            startingLoyalty: StartingLoyalty,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Jace });

        jace.SetOwner(owner);
        jace.SetController(owner);

        // -- +2: Look at the top card of target player's library. You may
        //    put that card on the bottom of that player's library. --------
        // CR 606 (loyalty) + CR 115 (target player) + CR 701.20 (zone move).
        // The target player is chosen by the activating player's agent via a
        // TargetRequest (any player — "target player", CR 115.4). The body
        // reads the CHOSEN player off the ResolutionContext (slot 0) with a
        // CR 608.2b legality re-check, falling back to the captured
        // targetPlayerResolver only on the legacy direct-activation path. v1
        // auto-accepts the optional "may bottom" — sending top → bottom is the
        // heuristic default (filter the opponent's draw, or scry your own).
        var peekPlayerRequest = new TargetRequest(
            Description: "Target player (look at top card; may bottom it)",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.None,
            CandidateGatherer: gameCtx => gameCtx.AllPlayers.Cast<object>().ToList());

        jace.AddAbility(new LoyaltyAbility(
            jace,
            +2,
            new[]
            {
                Fx.Inline("Look at top card of target player's library; may bottom it", rc =>
                {
                    var target = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                        ? rc.ChosenTargets[0][0] as Player
                        : null)
                        ?? targetPlayerResolver?.Invoke()?.FirstOrDefault();
                    if (target == null) return default;
                    var top = target.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null) return default;
                    // "Look at the top card" is a PRIVATE peek (CR 701.15) —
                    // only the activating player sees it — so no CardRevealedEvent
                    // is published (that public-reveal surface is reserved for
                    // "you may reveal" abilities, CR 701.16). "May put on bottom"
                    // — auto-accept the move. CR 400.7 — route the Library→Library
                    // bottom hop through the registered ZoneService so a
                    // CardMovedEvent publishes (the movement-provenance fix);
                    // raw-zone fallback when no service is registered.
                    BottomHop(target, top);
                    return default;
                }),
            },
            targetRequests: new[] { peekPlayerRequest }));

        // -- 0: Draw three cards, then put two cards from your hand on top
        //    of your library in any order. ----------------------------------
        // CR 606 (loyalty 0 — cost is "0", treated as no-op cost beyond
        // activation gate) + CR 121 (draw) + CR 701.20 (hand → library
        // top). Printed-order resolution (CR 608.2c) — draw first, then
        // return.
        jace.AddAbility(new LoyaltyAbility(jace, 0, () =>
        {
            var controller = jace.Controller ?? owner;
            // 1. Draw three (CR 121). DrawCards is idempotent on empty
            //    library (stamps the loss condition, halts the loop).
            Fx.DrawCards(controller, ZeroDrawCount);

            // 2. Put two cards from hand on top of library "in any order".
            //    v1: deterministic first-two-in-hand pick; both go to
            //    library index 0 in reverse order so the SECOND pick
            //    ends up underneath (matching "in any order" as a stable
            //    LIFO when the agent hasn't expressed a preference).
            var hand = controller.Zones.Hand.GetCards()
                .Take(ZeroTopReturnCount)
                .ToList();
            // Iterate forward, insert at top each time → forward order
            // ends up flipped on the library (first pick lands deepest).
            foreach (var c in hand)
            {
                controller.Zones.Hand.RemoveCard(c);
                controller.Zones.Library.InsertCardAt(0, c);
                c.SetZone(ZoneType.Library);
            }
        }));

        // -- -1: Return target creature to its owner's hand. ---------------
        // CR 606 (loyalty) + CR 115 (target creature) + CR 701.20 (bounce).
        // The target creature is chosen by the activating player's agent via a
        // TargetRequest (any battlefield creature — "target creature" is
        // unrestricted, CR 115.4). The body reads the CHOSEN creature off the
        // ResolutionContext (slot 0) with a CR 608.2b legality re-check,
        // falling back to the captured targetCreatureResolver only on the
        // legacy direct-activation path. Routes through Fx.BounceToHand so the
        // move respects owner-resolution (CR 400.3 — cards return to their
        // owner's zone, not the controller's).
        var bounceRequest = new TargetRequest(
            Description: "Return target creature to its owner's hand",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Removal,
            CandidateGatherer: gameCtx => gameCtx.AllPlayers
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .OfType<Creature>()
                .Cast<object>()
                .ToList());

        jace.AddAbility(new LoyaltyAbility(
            jace,
            -1,
            new[]
            {
                Fx.Inline("Return target creature to its owner's hand", rc =>
                {
                    var target = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                        ? rc.ChosenTargets[0][0] as Creature
                        : null)
                        ?? targetCreatureResolver?.Invoke()?.FirstOrDefault();
                    if (target == null) return default;
                    // CR 608.2b — a creature that has left the battlefield
                    // before resolution is an illegal target; the ability does
                    // nothing to it (the loyalty cost was already paid).
                    if (target.Zone != ZoneType.Battlefield) return default;
                    Fx.BounceToHand(target);
                    return default;
                }),
            },
            targetRequests: new[] { bounceRequest }));

        // -- -12: Exile all cards from target player's library, then that
        //    player shuffles their hand into their library. ----------------
        // CR 606 (loyalty) + CR 115 (target player) + CR 701.20 (library →
        // exile bulk) + CR 701.20 (hand → library bulk) + CR 701.20a (shuffle
        // via LibraryShuffle). Three-step printed order (CR 608.2c). The
        // target player is chosen by the activating player's agent via a
        // TargetRequest; the body reads the CHOSEN player off the
        // ResolutionContext (slot 0), falling back to the captured resolver
        // only on the legacy direct-activation path.
        var ultimatePlayerRequest = new TargetRequest(
            Description: "Exile target player's library; they shuffle hand into library",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.None,
            CandidateGatherer: gameCtx => gameCtx.AllPlayers.Cast<object>().ToList());

        jace.AddAbility(new LoyaltyAbility(
            jace,
            -12,
            new[]
            {
                Fx.Inline("Exile target player's library; they shuffle hand into library", rc =>
                {
                    var target = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                        ? rc.ChosenTargets[0][0] as Player
                        : null)
                        ?? targetPlayerResolver?.Invoke()?.FirstOrDefault();
                    if (target == null) return default;

                    // 1. Bulk move library → exile. Snapshot first (GetCards
                    //    returns a copy, but be explicit) to avoid mutating the
                    //    iterated collection. CR 400.7 — each hop is its own zone
                    //    change, so each is routed through the registered
                    //    ZoneService and publishes a CardMovedEvent (the
                    //    movement-provenance fix); raw fallback when unregistered.
                    var zones = ZoneServiceRegistry.Get(target);
                    var libSnapshot = target.Zones.Library.GetCards().ToList();
                    foreach (var c in libSnapshot)
                    {
                        Hop(zones, target, c, ZoneType.Library, ZoneType.Exile);
                    }

                    // 2. Bulk move hand → library.
                    var handSnapshot = target.Zones.Hand.GetCards().ToList();
                    foreach (var c in handSnapshot)
                    {
                        Hop(zones, target, c, ZoneType.Hand, ZoneType.Library);
                    }

                    // 3. Shuffle (CR 701.20a). Routes through GameRandomRegistry
                    //    + EventBusRegistry — LibraryShuffledEvent fires when a
                    //    bus is registered for the target.
                    LibraryShuffle.ShuffleLibrary(
                        target, reason: $"{CardName} -12 ultimate");
                    return default;
                }),
            },
            targetRequests: new[] { ultimatePlayerRequest }));

        return jace;
    }

    /// <summary>
    /// CR 400.7 — move a single card between two of <paramref name="player"/>'s
    /// zones. Routes through the registered <see cref="ZoneService"/> (when
    /// <paramref name="zones"/> is non-null) so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes for the hop —
    /// the movement-provenance fix for -12's library→exile / hand→library bulk
    /// legs. Falls back to raw zone manipulation (the prior behaviour) when no
    /// service is registered (shape / dispatcher-test paths), preserving the
    /// exact end state without events.
    /// </summary>
    private static void Hop(
        ZoneService? zones, Player player, ICard card, ZoneType from, ZoneType to)
    {
        if (card.Zone != from) return; // defensive: snapshot drift
        if (zones != null)
        {
            // Owner-routed (CR 400.3) — these cards never change owner.
            zones.MoveCard(card, from, to, player);
            return;
        }
        player.Zones.GetZone(from).RemoveCard(card);
        player.Zones.GetZone(to).AddCard(card);
        card.SetZone(to);
    }

    /// <summary>
    /// +2's "put that card on the bottom of that player's library" — a
    /// Library→Library reorder. Routed through the registered
    /// <see cref="ZoneService"/> so a <see cref="Majik.Core.Events.CardMovedEvent"/>
    /// fires (movement provenance); raw fallback re-appends to the bottom when no
    /// service is registered. <see cref="Zones.IZone.AddCard"/> appends, so both
    /// paths land the card on the bottom.
    /// </summary>
    private static void BottomHop(Player player, ICard top)
    {
        var zones = ZoneServiceRegistry.Get(player);
        if (zones != null)
        {
            zones.MoveCard(top, ZoneType.Library, ZoneType.Library, player);
            return;
        }
        player.Zones.Library.RemoveCard(top);
        player.Zones.Library.AddCard(top); // AddCard appends → bottom.
        top.SetZone(ZoneType.Library);
    }
}
