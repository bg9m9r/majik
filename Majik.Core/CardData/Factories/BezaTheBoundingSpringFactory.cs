using System.Linq;
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
/// Named-card factory for Beza, the Bounding Spring (Bloomburrow, {2}{W}{W}).
/// Legendary Creature — Elemental Elk 4/5. Oracle text (verified against
/// Scryfall 2026-06-23):
///   "When Beza enters, create a Treasure token if an opponent controls more
///    lands than you. You gain 4 life if an opponent has more life than you.
///    Create two 1/1 blue Fish creature tokens if an opponent controls more
///    creatures than you. Draw a card if an opponent has more cards in hand
///    than you."
///
/// The base shape (name, Legendary Creature, Elemental/Elk subtypes, {2}{W}{W},
/// 4/5) is materialised from the embedded JSON definition
/// (<c>beza-the-bounding-spring.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The four-clause ETB lives in the
/// factory — the JSON <c>AbilityDefinition</c> schema expresses neither token
/// creation nor per-clause "if an opponent has more X than you" intervening-ifs
/// (same posture as <see cref="KnightOfTheWhiteOrchidFactory"/>, whose opponent-
/// board-conditional ETB also lives in the factory).
///
/// ## Implemented (v1)
/// - 4/5 Legendary Creature — Elemental Elk at {2}{W}{W} (mana value 4),
///   owner/controller wired (from JSON).
/// - <b>ETB triggered ability (CR 603.1)</b> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with FOUR independent
///   clauses, each gated by its own intervening-style condition evaluated at
///   RESOLUTION off the live <see cref="ResolutionContext.Game"/> (NOT a
///   captured resolver — the production routed build threads the live
///   GameContext through <see cref="TriggeredAbility.ResolveAsync"/>; mirrors
///   the Knight of the White Orchid #2540 prod-path fix). Each comparison is
///   STRICT ("MORE than you" — a tie does not satisfy it, CR 603.4 wording):
///   <list type="number">
///     <item><description><b>Treasure</b> — "if an opponent controls more
///     lands than you" → one Treasure token (CR 111.10) via
///     <see cref="TokenFactory.CreateTreasure"/>.</description></item>
///     <item><description><b>Life</b> — "you gain 4 life if an opponent has
///     more life than you" → <see cref="Fx.GainLife"/> 4 (CR 119.3).</description></item>
///     <item><description><b>Fish</b> — "create two 1/1 blue Fish creature
///     tokens if an opponent controls more creatures than you" → two
///     <see cref="TokenFactory.CreateOnBattlefield"/> tokens stamped
///     <see cref="ManaColor.Blue"/> (CR 111.4).</description></item>
///     <item><description><b>Draw</b> — "draw a card if an opponent has more
///     cards in hand than you" → <see cref="Fx.DrawCards"/> 1 (CR 120.2).</description></item>
///   </list>
///   Each clause's condition is re-read independently, so a single resolution
///   can fire any subset of the four (CR 608.2 — the resolving ability does
///   everything its text says, in order).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + ETB trigger attached. The four
///   comparisons read the live resolution context, so they are correct on the
///   production routed build. This is the overload
///   <see cref="NamedCardFactory"/> / the routed prod build dispatches to; the
///   live engine registers the trigger via <c>TriggerManager.BindCard</c> when
///   Beza enters.
/// - <see cref="Create(Player, TriggerManager?)"/> — additionally registers the
///   ETB trigger with the supplied <see cref="TriggerManager"/> for tests that
///   drive the bus-fired trigger path directly.
/// </summary>
[CardName("Beza, the Bounding Spring")]
public static class BezaTheBoundingSpringFactory
{
    public const string CardName = "Beza, the Bounding Spring";
    public const string Slug = "beza-the-bounding-spring";

    /// <summary>
    /// Shape-only overload — ETB trigger attached without registering with a
    /// <see cref="TriggerManager"/>. The four opponent-board comparisons read
    /// the live resolution context, so they are correct on the production
    /// routed build. This is the overload <see cref="NamedCardFactory"/> / the
    /// routed prod build dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Beza with its four-clause ETB trigger attached and optionally
    /// registered against the supplied <paramref name="triggers"/> manager.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers so a
    /// qualifying <see cref="Majik.Core.Events.CardMovedEvent"/> queues the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature, Elemental/Elk subtypes, {2}{W}{W}, 4/5). The JSON carries
        // no abilities — the four-clause ETB is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // --------------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When Beza enters, create a Treasure token if an opponent controls
        //    more lands than you. You gain 4 life if an opponent has more life
        //    than you. Create two 1/1 blue Fish creature tokens if an opponent
        //    controls more creatures than you. Draw a card if an opponent has
        //    more cards in hand than you."
        //
        // Each of the four clauses has its OWN condition, evaluated at
        // RESOLUTION off the live ctx.Game (CR 608.2). Every comparison is
        // STRICT ("more ... than you"); a tie does not satisfy it. The live
        // context is the authoritative read — the production routed build
        // (GameFacade → NamedCardFactory.Create) threads the GameContext
        // through ResolveAsync, so reading ctx.Game (not a captured resolver)
        // keeps the routed build correct (Knight of the White Orchid #2540).
        // --------------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: four opponent-comparison ETB clauses",
            ctx =>
            {
                var controller = card.Controller ?? owner;

                // Routes spawned tokens through the live ZoneService so
                // CardMovedEvent fires for downstream ETB listeners. Null on
                // shape-only paths — TokenFactory falls back to a direct add.
                var zones = ZoneServiceRegistry.Get(controller);

                // Clause 1 — CR 111.10: Treasure if out-landed.
                if (AnOpponentHasMore(ctx, controller, CountLands))
                {
                    TokenFactory.CreateTreasure(controller, zones);
                }

                // Clause 2 — CR 119.3: gain 4 life if behind on life.
                if (AnOpponentHasMore(ctx, controller, p => p.LifeTotal))
                {
                    Fx.GainLife(controller, 4);
                }

                // Clause 3 — CR 111.4: two 1/1 blue Fish if out-creatured.
                if (AnOpponentHasMore(ctx, controller, CountCreatures))
                {
                    var spec = new TokenFactory.TokenSpec(
                        Name: "Fish",
                        Power: 1,
                        Toughness: 1,
                        Subtypes: new[] { CardSubtype.Fish },
                        Colors: new[] { ManaColor.Blue });
                    TokenFactory.CreateOnBattlefield(spec, controller, zones);
                    TokenFactory.CreateOnBattlefield(spec, controller, zones);
                }

                // Clause 4 — CR 120.2: draw a card if behind on hand size.
                if (AnOpponentHasMore(ctx, controller, p => p.Zones.Hand.GetCards().Count()))
                {
                    Fx.DrawCards(controller, 1);
                }

                return ValueTask.CompletedTask;
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// CR 603.4 — true iff at least one opponent's value of
    /// <paramref name="selector"/> is STRICTLY greater than the controller's
    /// ("more ... than you" — a tie does not satisfy it). Reads opponents off
    /// the live <see cref="ResolutionContext.Game"/> (CR 102.1 — the controller
    /// is never their own opponent; lost players are skipped). Returns false
    /// when there is no live game context (shape-only paths), so every clause
    /// is a safe no-op off the battlefield.
    /// </summary>
    private static bool AnOpponentHasMore(
        ResolutionContext ctx, Player controller, Func<Player, int> selector)
    {
        var players = ctx.Game?.AllPlayers;
        if (players == null) return false;

        var mine = selector(controller);
        foreach (var opp in players)
        {
            if (ReferenceEquals(opp, controller)) continue; // CR 102.1
            if (opp.HasLost) continue;
            if (selector(opp) > mine) return true;
        }
        return false;
    }

    /// <summary>CR 305 — land permanents <paramref name="player"/> controls.</summary>
    private static int CountLands(Player player) =>
        player.Zones.Battlefield.GetCards().Count(c => c.HasType(CardType.Land));

    /// <summary>CR 302 — creature permanents <paramref name="player"/> controls.</summary>
    private static int CountCreatures(Player player) =>
        player.Zones.Battlefield.GetCards().Count(c => c.HasType(CardType.Creature));
}
