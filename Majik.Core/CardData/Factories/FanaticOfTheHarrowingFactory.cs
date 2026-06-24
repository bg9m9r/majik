using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fanatic of the Harrowing (Outlaws of Thunder
/// Junction, {3}{B}). Creature — Human Cleric 2/2.
///
/// ## Card text (Scryfall verified)
/// "When this creature enters, each player discards a card. If you discarded a
///  card this way, draw a card."
///
/// ## Base shape
/// Name / Creature / Human Cleric / {3}{B} / 2/2 are materialised from the
/// embedded JSON definition (<c>fanatic-of-the-harrowing.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="PlaguecrafterFactory"/>. The ETB behaviour is layered on here
/// because the JSON ability schema doesn't express the
/// each-player-discard-with-conditional-draw rider.
///
/// ## Implemented (v1)
/// - <b>ETB triggered ability (CR 603.1)</b>: wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> — same trigger shape as
///   <see cref="PlaguecrafterFactory"/>.
/// - <b>"Each player discards a card"</b> (CR 701.8). Unlike "each opponent"
///   effects, Fanatic affects EACH player — the controller included
///   (CR 109.5 / 800.4). Each affected player discards a card "of their choice"
///   (CR 701.8a): that player's agent drives the pick (intent
///   <see cref="BotIntent.Discard"/>), with a deterministic first-card fallback
///   (mirrors <see cref="PlaguecrafterFactory"/>). A player with an empty hand
///   discards nothing (CR 701.8c).
/// - <b>"If you discarded a card this way, draw a card"</b> (CR 603 conditional
///   rider, evaluated at resolution). "You" = the trigger's controller
///   (<c>ctx.Controller</c>). The draw fires only when the controller actually
///   discarded a card during this resolution (a non-empty hand at the moment
///   they were iterated) — a controller with an empty hand discards nothing
///   and therefore does NOT draw. The draw routes through
///   <see cref="Fx.DrawCards"/> (CR 120 / 614 — replacement-aware).
///
/// ## Sequencing (CR 608.2 / APNAP 101.4)
/// Each player discards as the iteration reaches them. The discards are
/// independent (no shared zones) so iteration order is unobservable; the
/// conditional draw is evaluated from whether the controller discarded.
/// The body reads every player from the LIVE resolution context
/// (<c>ctx.Game.AllPlayers</c>) at resolution — no captured player resolver, so
/// it is correct on the production routed build (mirrors
/// <see cref="PlaguecrafterFactory"/>). With no live game context the body
/// no-ops cleanly (shape-only paths).
///
/// ## Deferred (v1 gaps)
/// - <b>Discard prompt UI</b>: each affected player's agent receives the full
///   hand; surfacing the choice to the portal decision panel is deferred —
///   same queue as <see cref="PlaguecrafterFactory"/>.
/// </summary>
[CardName("Fanatic of the Harrowing")]
public static class FanaticOfTheHarrowingFactory
{
    public const string CardName = "Fanatic of the Harrowing";
    public const string Slug = "fanatic-of-the-harrowing";

    /// <summary>
    /// Construct Fanatic of the Harrowing with no live wiring. The ETB trigger
    /// is attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>); its body no-ops cleanly without a live
    /// game context. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, agent: null);

    /// <summary>
    /// Construct Fanatic of the Harrowing with optional runtime services. The
    /// ETB "each player discards … / you draw" body reads every player from the
    /// LIVE resolution context (<c>ctx.Game.AllPlayers</c>) at resolution. Each
    /// affected player's discard pick reads THAT player's agent from
    /// <see cref="AgentRegistry"/> (the optional <paramref name="agent"/>
    /// overrides it for tests).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager — when supplied the ETB trigger is
    /// registered so the enter-battlefield event lands it on the stack
    /// automatically.</param>
    /// <param name="agent">Optional agent override driving each affected
    /// player's discard pick. Null reads each affected player's live agent from
    /// <see cref="AgentRegistry"/>, then falls back to a deterministic
    /// first-card pick.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human
        // Cleric, {3}{B}, 2/2). No abilities in the JSON — the ETB behaviour is
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When this creature enters, each player discards a card. If you
        //    discarded a card this way, draw a card."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: each player discards a card; if you discarded one this way, draw a card",
            ctx =>
            {
                Resolve(ctx.Controller, ctx.Game?.AllPlayers, agent);
                return ValueTask.CompletedTask;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // -----------------------------------------------------------------------
    // Resolution body — CR 701.8 (each player discards) + CR 603 conditional
    // rider (if YOU discarded this way, draw a card).
    // -----------------------------------------------------------------------
    private static void Resolve(
        Player controller,
        IReadOnlyList<Player>? players,
        IPlayerAgent? agentOverride)
    {
        if (players == null) return; // shape path — no live game.

        var controllerDiscarded = false;

        foreach (var pl in players)
        {
            if (pl == null) continue;

            // The affected player's own agent drives "of their choice": the
            // explicit test override first, otherwise the live per-seat agent
            // registered in AgentRegistry (mirrors PlaguecrafterFactory).
            var agent = agentOverride ?? AgentRegistry.Get(pl);

            var discarded = DiscardOfTheirChoice(pl, agent);
            if (discarded && ReferenceEquals(pl, controller))
            {
                controllerDiscarded = true;
            }
        }

        // CR 603 — "If you discarded a card this way, draw a card." The draw is
        // replacement-aware (CR 120 / 614) via Fx.DrawCards.
        if (controllerDiscarded)
        {
            Fx.DrawCards(controller, 1);
        }
    }

    /// <summary>
    /// CR 701.8 — <paramref name="player"/> discards a card of their choice.
    /// An empty hand → no discard (CR 701.8c). The discarding player chooses
    /// (agent-driven, deterministic first-card fallback). Returns
    /// <see langword="true"/> when a card was actually discarded.
    /// </summary>
    private static bool DiscardOfTheirChoice(Player player, IPlayerAgent? agent)
    {
        var hand = player.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return false; // can't discard.

        ICard pick;
        if (agent != null)
        {
            var chosen = agent
                .ChooseFromHandAsync(player, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                .GetAwaiter().GetResult();
            pick = (chosen != null && chosen.Zone == ZoneType.Hand) ? chosen : hand[0];
        }
        else
        {
            pick = hand[0];
        }

        player.Zones.Hand.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        return true;
    }
}
