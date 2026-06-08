using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grist, the Hunger Tide (Modern Horizons 2, {1}{B}{G}).
///
/// Legendary Planeswalker — Grist, loyalty 3.
/// Oracle text (Scryfall, verified 2026-06-08):
///   "As long as Grist isn't on the battlefield, it's a 1/1 Insect creature in
///        addition to its other types.
///    +1: Create a 1/1 black and green Insect creature token, then mill a card.
///        If an Insect card was milled this way, put a loyalty counter on Grist
///        and repeat this process.
///    −2: You may sacrifice a creature. When you do, destroy target creature or
///        planeswalker.
///    −5: Each opponent loses life equal to the number of creature cards in your
///        graveyard."
///
/// The base shape (name, Legendary Planeswalker — Grist, {1}{B}{G}, loyalty 3,
/// plus the V1 Creature-type simplification — see below) is materialised from
/// the fluent <see cref="CardDef"/> DSL via <see cref="Define"/>; the three
/// loyalty abilities are layered on in <see cref="Create(Player)"/> (same
/// posture as <see cref="KothOfTheHammerFactory"/> /
/// <see cref="LilianaTheLastHopeFactory"/> — the DSL has no vocabulary for
/// loyalty abilities, so they live in the factory). The source generator routes
/// dispatch to <see cref="Create(Player)"/> in preference to <see cref="Define"/>
/// because a <c>Create(Player)</c> overload exists, so the loyalty abilities are
/// present in prod (not only in the fluent shape-only path).
///
/// ## CDA — 1/1 Insect off the battlefield (V1 simplification)
/// The oracle's characteristic-defining ability ("As long as Grist isn't on the
/// battlefield, it's a 1/1 Insect creature in addition to its other types",
/// CR 604.3) is approximated, not fully modelled. Grist is constructed as a
/// Planeswalker with <see cref="CardType.Creature"/> added UNCONDITIONALLY plus
/// the Insect subtype, so creature-search tutors (Green Sun's Zenith, Chord of
/// Calling) find Grist in the library — the practical reason the CDA matters.
/// The conditional "only while not on the battlefield" half (a CDA that toggles
/// the Creature type off once Grist enters, and stamps a 1/1 body in other
/// zones) needs a zone-conditional layer-4/7b CDA primitive the engine doesn't
/// have — CDAs today only apply on the battlefield (Tarmogoyf / Death's Shadow).
/// That conditional toggle is the remaining deferred surface (see
/// <see cref="KnownPartialImplementations"/>).
///
/// ## Loyalty abilities — implemented (V1)
/// - <b>+1: Create a 1/1 black and green Insect token, then mill a card. If an
///   Insect card was milled this way, put a loyalty counter on Grist and repeat
///   (CR 606 + CR 111 token + CR 701.13 mill + CR 122 counters)</b>: fully
///   implemented as a loop — mints the Insect token via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, ZoneService?)"/>
///   (ETB events fire when the per-game <see cref="ZoneService"/> is wired),
///   then mills one card via <see cref="Fx.Mill(Player, int)"/>. While the
///   milled card has the Insect subtype the source gains a loyalty counter
///   (<see cref="Planeswalker.AddLoyalty"/>) and the process repeats. An empty
///   library ends the loop (the mill returns nothing — not an Insect).
/// - <b>−5: Each opponent loses life equal to the number of creature cards in
///   your graveyard (CR 606 + CR 119 life loss)</b>: counts the creature cards
///   in the controller's graveyard and applies that life loss to each opponent
///   returned by <paramref name="opponentsResolver"/> via
///   <see cref="Fx.LoseLife(Player, int)"/>. Zero creature cards ⇒ no life lost
///   (CR 119.3 — losing 0 life is still an event but changes nothing).
///
/// ## Loyalty abilities — V1 simplifications
/// - <b>−2: You may sacrifice a creature. When you do, destroy target creature
///   or planeswalker (CR 606 + CR 701.17 sacrifice + CR 603.3 reflexive trigger
///   + CR 701.7 destroy)</b>: implemented deterministically through resolvers —
///   the creature to sacrifice comes from <paramref name="sacrificeResolver"/>
///   and the permanent to destroy from <paramref name="destroyTargetResolver"/>.
///   The "When you do" reflexive trigger (CR 603.3) is flattened: if a sacrifice
///   is chosen, it is performed and the destroy follows in the same resolution.
///   No sacrifice chosen ⇒ the whole clause is skipped ("You MAY sacrifice").
///   v1 has no agent target / sacrifice prompt for loyalty abilities (same gap
///   Koth / Liliana / Chandra share); a null resolver no-ops that half.
/// </summary>
[CardName("Grist, the Hunger Tide")]
public static class GristFactory
{
    public const string CardName = "Grist, the Hunger Tide";
    public const string Cost = "{1}{B}{G}";
    public const int StartingLoyalty = 3;
    public const int Plus1Loyalty = +1;
    public const int Minus2Loyalty = -2;
    public const int Minus5Loyalty = -5;

    /// <summary>The 1/1 black-and-green Insect token the +1 creates.</summary>
    public const int InsectTokenPower = 1;
    public const int InsectTokenToughness = 1;

    /// <summary>
    /// Fluent base shape — Legendary Planeswalker — Grist, {1}{B}{G}, loyalty 3,
    /// plus the V1 unconditional Creature type + Insect subtype (CDA
    /// approximation; see class xmldoc). Loyalty abilities are NOT expressible
    /// here; they are layered on in <see cref="Create(Player)"/>.
    /// </summary>
    public static CardDef Define() => CardDef
        .Planeswalker(CardName, Cost, loyalty: StartingLoyalty)
        .WithSupertype(CardSupertype.Legendary)
        .WithSubtypes(CardSubtype.Grist, CardSubtype.Insect)
        // V1 CDA approximation: add Creature type unconditionally so tutors like
        // Green Sun's Zenith can target Grist in all zones (CR 115.4 / 106.5a).
        // The conditional "only while not on the battlefield" toggle is the
        // deferred surface (see class xmldoc / KnownPartialImplementations).
        .WithType(CardType.Creature);

    /// <summary>
    /// Construct Grist with no resolvers / zone service wired — the +1 still
    /// creates a token (no ETB events without a ZoneService) and mills/repeats,
    /// the −2 no-ops its sacrifice/destroy (no resolvers), and the −5 no-ops its
    /// life loss (no opponents resolver). Loyalty changes still apply. Suitable
    /// for shape / dispatcher tests. This is the overload the source generator
    /// dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner,
            zones: null,
            sacrificeResolver: null,
            destroyTargetResolver: null,
            opponentsResolver: null);

    /// <summary>
    /// Construct Grist, the Hunger Tide with the live game services / resolvers.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">Zone service for the +1's token ETB + mill events.
    /// May be null — the token still enters and the mill still happens, but no
    /// CardMovedEvent fires.</param>
    /// <param name="sacrificeResolver">Returns the creature the −2 sacrifices.
    /// v1 picks the first. May be null — the −2 clause is skipped ("you MAY
    /// sacrifice").</param>
    /// <param name="destroyTargetResolver">Returns the creature / planeswalker
    /// the −2 destroys after the sacrifice. v1 picks the first. May be null —
    /// the destroy is skipped.</param>
    /// <param name="opponentsResolver">Returns the −5's opponents (life-loss
    /// recipients). May be null — the −5 loses Grist nothing.</param>
    public static Planeswalker Create(
        Player owner,
        ZoneService? zones,
        Func<IReadOnlyList<Creature>>? sacrificeResolver,
        Func<IReadOnlyList<Permanent>>? destroyTargetResolver,
        Func<IReadOnlyList<Player>>? opponentsResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var grist = (Planeswalker)CardDefRuntime.Build(Define(), owner);

        // -- +1: Create a 1/1 black and green Insect creature token, then mill a
        //    card. If an Insect card was milled this way, put a loyalty counter
        //    on Grist and repeat this process. ----------------------------------
        // CR 606 (loyalty) + CR 111 (token) + CR 701.13 (mill) + CR 122
        // (loyalty counters). The loop terminates when a non-Insect card is
        // milled or the library is empty (the mill returns nothing).
        grist.AddAbility(new LoyaltyAbility(grist, Plus1Loyalty, () =>
        {
            var controller = grist.Controller ?? owner;

            while (true)
            {
                // Create a 1/1 black and green Insect creature token.
                var spec = new TokenFactory.TokenSpec(
                    Name: "Insect",
                    Power: InsectTokenPower,
                    Toughness: InsectTokenToughness,
                    Subtypes: new[] { CardSubtype.Insect },
                    // CR 105 / CR 111.4 — black AND green token.
                    Colors: new[] { ManaColor.Black, ManaColor.Green });
                TokenFactory.CreateOnBattlefield(spec, controller, zones);

                // Mill a card (CR 701.13). Fx.Mill returns the milled cards.
                var milled = Fx.Mill(controller, 1);

                // "If an Insect card was milled this way" — repeat only when the
                // single milled card has the Insect subtype. Empty mill (library
                // empty) ends the loop.
                var insectMilled = milled.Count > 0
                    && milled[0].HasSubtype(CardSubtype.Insect);
                if (!insectMilled) return;

                // Put a loyalty counter on Grist, then repeat (CR 122 / CR 606).
                grist.AddLoyalty(1);
            }
        }));

        // -- −2: You may sacrifice a creature. When you do, destroy target
        //    creature or planeswalker. ------------------------------------------
        // CR 606 (loyalty) + CR 701.17 (sacrifice) + CR 603.3 (reflexive "when
        // you do" trigger, flattened here) + CR 701.7 (destroy). v1
        // deterministic via resolvers; "you MAY sacrifice" → no sacrifice chosen
        // skips the whole clause (the destroy is gated on the sacrifice).
        grist.AddAbility(new LoyaltyAbility(grist, Minus2Loyalty, () =>
        {
            var toSacrifice = sacrificeResolver?.Invoke()?.FirstOrDefault();
            if (toSacrifice == null) return;
            if (toSacrifice.Zone != ZoneType.Battlefield) return;

            // CR 701.17 — sacrifice the chosen creature.
            Fx.Sacrifice(toSacrifice);

            // CR 603.3 — "When you do, destroy target creature or planeswalker."
            // The reflexive trigger only fires because a sacrifice happened.
            var toDestroy = destroyTargetResolver?.Invoke()?.FirstOrDefault();
            if (toDestroy == null) return;
            if (toDestroy.Zone != ZoneType.Battlefield) return;
            // CR 701.7 — "destroy" routes through the indestructible /
            // regeneration gate via the Destroy zone-move reason.
            Fx.MoveToGraveyard(toDestroy, ZoneMoveReason.Destroy);
        }));

        // -- −5: Each opponent loses life equal to the number of creature cards
        //    in your graveyard. -------------------------------------------------
        // CR 606 (loyalty) + CR 119 (life loss). Counts the creature CARDS in
        // the controller's graveyard (CR 205.2a — type read off the card) and
        // applies that loss to each opponent. Zero ⇒ no change (CR 119.3).
        grist.AddAbility(new LoyaltyAbility(grist, Minus5Loyalty, () =>
        {
            var controller = grist.Controller ?? owner;
            var creatureCards = controller.Zones.Graveyard.GetCards()
                .Count(c => c.HasType(CardType.Creature));
            if (creatureCards <= 0) return; // CR 119.3 — losing 0 life is a no-op.

            var opponents = opponentsResolver?.Invoke();
            if (opponents == null) return;
            foreach (var opponent in opponents)
            {
                if (opponent == null) continue;
                Fx.LoseLife(opponent, creatureCards);
            }
        }));

        return grist;
    }
}
