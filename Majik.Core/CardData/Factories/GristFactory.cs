using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
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
/// ## CDA — 1/1 Insect off the battlefield (fully modelled)
/// The oracle's characteristic-defining ability ("As long as Grist isn't on the
/// battlefield, it's a 1/1 Insect creature in addition to its other types",
/// CR 604.3) is implemented as a zone-conditional CDA via
/// <see cref="Card.SetOffBattlefieldCharacteristics"/>: off the battlefield Grist
/// gains the <see cref="CardType.Creature"/> type, the Insect subtype, and a 1/1
/// body; on the battlefield it is ONLY a Planeswalker (no creature type, no
/// body). <see cref="Card.HasType"/> / <see cref="Card.HasSubtype"/> report the
/// granted characteristics in every zone except the battlefield, so creature
/// tutors (Green Sun's Zenith, Chord of Calling), reanimation, delirium, and
/// graveyard-creature-matters all see Grist where the CDA applies — and stop
/// seeing it the instant Grist resolves onto the battlefield.
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
/// ## Loyalty abilities — −2 (sacrifice + prompted destroy)
/// - <b>−2: You may sacrifice a creature. When you do, destroy target creature
///   or planeswalker (CR 606 + CR 701.17 sacrifice + CR 603.3 reflexive trigger
///   + CR 701.7 destroy)</b>: activated through the priority loop as a sorcery-
///   speed loyalty ability — the loyalty cost is paid as it is put on the stack
///   and the effect resolves off the stack (CR 606.3). The DESTROY half is a
///   real agent-chosen target: the ability declares a <see cref="TargetRequest"/>
///   (gathering every battlefield creature / planeswalker), the dispatch path
///   prompts the activating player, and the effect destroys the CHOSEN permanent
///   read off the <see cref="Abilities.ResolutionContext"/> (falling back to
///   <paramref name="destroyTargetResolver"/> only on the legacy direct-activation
///   path). The creature to sacrifice comes from <paramref name="sacrificeResolver"/>
///   ("You MAY sacrifice" — a choice, not a target); no sacrifice chosen ⇒ the
///   whole clause is skipped, and the "When you do" reflexive trigger (CR 603.3)
///   is flattened: the destroy follows the sacrifice in the same resolution.
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
        // Grist (the planeswalker subtype) is printed and present in every zone.
        // The Insect subtype + Creature type are NOT printed on the battlefield —
        // they come from the zone-conditional CDA, applied in Create(...) via
        // SetOffBattlefieldCharacteristics (CR 604.3).
        .WithSubtype(CardSubtype.Grist);

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

        // CR 604.3 — "As long as Grist isn't on the battlefield, it's a 1/1
        // Insect creature in addition to its other types." A zone-conditional
        // characteristic-defining ability: off the battlefield Grist gains the
        // Creature type + Insect subtype and a 1/1 body (so reanimation, "creature
        // card" tutors like Green Sun's Zenith, delirium, and graveyard-creature
        // matters all see it); on the battlefield it is ONLY a Planeswalker.
        grist.SetOffBattlefieldCharacteristics(
            types: new[] { CardType.Creature },
            subtypes: new[] { CardSubtype.Insect },
            power: 1,
            toughness: 1);

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
        // you do" trigger, flattened here) + CR 701.7 (destroy). The DESTROY
        // half is a real target chosen by the activating player's agent: a
        // TargetRequest is declared so the dispatch path prompts for it and the
        // effect reads the chosen permanent off the ResolutionContext (CR 602.2b
        // / 608.2g) — not a deterministic resolver. The sacrifice ("you MAY")
        // remains a choice supplied by sacrificeResolver; no sacrifice chosen
        // skips the whole clause (the destroy is gated on the sacrifice).
        //
        // Candidates are gathered live at activation (every battlefield creature
        // or planeswalker) so the target reflects the board state when the
        // ability is put on the stack.
        var destroyRequest = new TargetRequest(
            Description: "Destroy target creature or planeswalker",
            MinTargets: 0, // "When you do" — the destroy only happens if a sacrifice was made.
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Removal,
            CandidateGatherer: gameCtx => gameCtx.AllPlayers
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .Where(c => c.HasType(CardType.Creature) || c.HasType(CardType.Planeswalker))
                .Cast<object>()
                .ToList());

        grist.AddAbility(new LoyaltyAbility(
            grist,
            Minus2Loyalty,
            new[]
            {
                Fx.Inline("You may sacrifice a creature; if you do, destroy target creature or planeswalker", rc =>
                {
                    var toSacrifice = sacrificeResolver?.Invoke()?.FirstOrDefault();
                    if (toSacrifice == null) return default;
                    if (toSacrifice.Zone != ZoneType.Battlefield) return default;

                    // CR 701.17 — sacrifice the chosen creature.
                    Fx.Sacrifice(toSacrifice);

                    // CR 603.3 — "When you do, destroy target creature or
                    // planeswalker." Prefer the agent-chosen target off the
                    // ResolutionContext (slot 0); fall back to the resolver on
                    // the legacy direct-activation path (no chosen targets).
                    // Fizzles if neither yields a target or it left the
                    // battlefield (CR 608.2b).
                    var toDestroy = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                        ? rc.ChosenTargets[0][0] as Permanent
                        : null)
                        ?? destroyTargetResolver?.Invoke()?.FirstOrDefault();
                    if (toDestroy == null) return default;
                    if (toDestroy.Zone != ZoneType.Battlefield) return default;
                    // CR 701.7 — "destroy" routes through the indestructible /
                    // regeneration gate via the Destroy zone-move reason.
                    Fx.MoveToGraveyard(toDestroy, ZoneMoveReason.Destroy);
                    return default;
                }),
            },
            targetRequests: new[] { destroyRequest }));

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
