using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
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
/// ## Deferred (v1 gaps)
/// - <b>Loyalty target prompts</b>: <see cref="LoyaltyAbility"/> doesn't
///   declare <see cref="Majik.Core.Targeting.TargetRequest"/>s. +2 / -1
///   / -12 pick from supplied resolvers deterministically; 0's "two
///   cards from your hand" uses first-in-hand. Agent-driven choice is
///   the same gap Karn / Wrenn / Ugin have.
/// - <b>ZoneService routing on -12 bulk moves</b>: -12 uses raw zone
///   manipulation, so <see cref="Majik.Core.Events.CardMovedEvent"/>
///   doesn't publish on the per-card hops. Same posture as Karn's -3 /
///   Ugin's -X / -10.
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
        // CR 606 (loyalty) + CR 701.20 (zone move). v1 auto-accepts the
        // optional "may bottom" — sending top → bottom is the heuristic
        // default (matches the most common play pattern: filter the
        // opponent's draw, or scry your own).
        jace.AddAbility(new LoyaltyAbility(jace, +2, () =>
        {
            var targets = targetPlayerResolver?.Invoke();
            if (targets == null) return;
            var target = targets.FirstOrDefault();
            if (target == null) return;
            var top = target.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            // "Look at" is internal — no visible state change for the peek
            // step (the engine doesn't yet model hidden-info reveals;
            // future agent-driven choice will read the snapshot).
            // "May put on bottom" — auto-accept the bottom move.
            target.Zones.Library.RemoveCard(top);
            target.Zones.Library.AddCard(top); // AddCard appends → bottom.
            top.SetZone(ZoneType.Library);
        }));

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
        // CR 606 (loyalty) + CR 701.20 (bounce). v1 picks the first target
        // candidate. Routes through Fx.BounceToHand so the move respects
        // owner-resolution (CR 400.3 — cards return to their owner's
        // zone, not the controller's).
        jace.AddAbility(new LoyaltyAbility(jace, -1, () =>
        {
            var candidates = targetCreatureResolver?.Invoke();
            if (candidates == null) return;
            var target = candidates.FirstOrDefault();
            if (target == null) return;
            if (target.Zone != ZoneType.Battlefield) return;
            Fx.BounceToHand(target);
        }));

        // -- -12: Exile all cards from target player's library, then that
        //    player shuffles their hand into their library. ----------------
        // CR 606 (loyalty) + CR 701.20 (library → exile bulk) + CR 701.20
        // (hand → library bulk) + CR 701.20a (shuffle via
        // LibraryShuffle). Three-step printed order (CR 608.2c).
        jace.AddAbility(new LoyaltyAbility(jace, -12, () =>
        {
            var targets = targetPlayerResolver?.Invoke();
            if (targets == null) return;
            var target = targets.FirstOrDefault();
            if (target == null) return;

            // 1. Bulk move library → exile. Snapshot first (GetCards
            //    returns a copy, but be explicit) to avoid mutating the
            //    iterated collection.
            var libSnapshot = target.Zones.Library.GetCards().ToList();
            foreach (var c in libSnapshot)
            {
                target.Zones.Library.RemoveCard(c);
                target.Zones.Exile.AddCard(c);
                c.SetZone(ZoneType.Exile);
            }

            // 2. Bulk move hand → library.
            var handSnapshot = target.Zones.Hand.GetCards().ToList();
            foreach (var c in handSnapshot)
            {
                target.Zones.Hand.RemoveCard(c);
                target.Zones.Library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }

            // 3. Shuffle (CR 701.20a). Routes through GameRandomRegistry
            //    + EventBusRegistry — LibraryShuffledEvent fires when a
            //    bus is registered for the target.
            LibraryShuffle.ShuffleLibrary(
                target, reason: $"{CardName} -12 ultimate");
        }));

        return jace;
    }
}
