using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.Definitions;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bellowing Crier (Murders at Karlov Manor,
/// {1}{U}).
///
/// Creature — Frog Advisor 2/1. Oracle text:
///   "When this creature enters, draw a card, then discard a card."
///
/// ## Implemented (v1)
/// - 2/1 Creature — Frog Advisor, mana cost {1}{U}. Colour identity blue
///   (derived from the {U} pip per CR 202.2c). Mana value 2 (CR 202.3).
///   Base shape comes from the embedded JSON definition (name, type,
///   subtypes, P/T, cost); the ETB ability is layered on below.
/// - <b>ETB mandatory loot</b> (CR 603.1 self-ETB trigger): "draw a card,
///   then discard a card." Unlike <see cref="ScrapworkMuttFactory"/>'s
///   "you may discard … if you do, draw" loot, this is MANDATORY and in the
///   reverse order — the draw happens first (CR 121.1), then the discard
///   (CR 701.8). The "then" sequences the two sub-effects as a single
///   resolution (CR 608.2 — instructions performed in order).
///   - <b>Draw</b> routes through <see cref="Fx.DrawCards"/> so the
///     replacement bus (Dredge / draw-count modifiers) and empty-library
///     SBA marking (CR 704.5c) are honoured.
///   - <b>Discard</b> routes through <see cref="Fx.DiscardCard"/> — the
///     central discard chokepoint that publishes a <c>DiscardedEvent</c>
///     (and honours Madness / discard replacements). The discarded card is
///     agent-chosen when an <see cref="IPlayerAgent"/> is in scope
///     (<see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///     <see cref="BotIntent.Discard"/>); agent-less / off-hand picks fall
///     back to the last card in hand (same posture as
///     <see cref="ScrapworkMuttFactory"/>). The discard is mandatory: if the
///     hand is non-empty after the draw it WILL happen.
///
/// ## Edge cases
/// - <b>Empty library on the draw</b>: <see cref="Fx.DrawCards"/> marks the
///   tried-to-draw-from-empty flag (CR 704.5c loss SBA) and draws nothing;
///   the discard then operates on the pre-existing hand (CR 608.2 — the
///   discard is not gated on the draw succeeding, unlike Scrapwork Mutt's
///   "if you do").
/// - <b>Empty hand after the draw</b> (e.g. drew the only card then nothing
///   else): the discard is a no-op — there is nothing to discard.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only, no agent / TriggerManager.
///   The ETB trigger is attached for shape inspection but not registered.
/// - <see cref="Create(Player, TriggerManager?, IPlayerAgent?)"/> —
///   registers the ETB trigger on the bus; the supplied agent drives the
///   discard pick.
/// </summary>
[CardName("Bellowing Crier")]
public static class BellowingCrierFactory
{
    public const string CardName = "Bellowing Crier";
    public const string Slug = "bellowing-crier";

    /// <summary>Shape-only construction (no agent / TriggerManager).</summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, agent: null);

    /// <summary>
    /// Construct Bellowing Crier. When <paramref name="triggers"/> is supplied
    /// the ETB loot trigger is registered on the bus; when
    /// <paramref name="agent"/> is supplied it drives the discard pick.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Frog Advisor, {1}{U}, 2/1). No abilities in the JSON — the ETB
        // loot is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "When this creature enters, draw a card, then discard a card."
        // CR 603.1 self-ETB trigger. The draw (CR 121.1) and discard
        // (CR 701.8) are performed in printed order as one resolution
        // (CR 608.2 — "then" sequences the instructions). Both are
        // MANDATORY — there is no "you may" here.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: draw a card, then discard a card",
            ctx => ResolveLootAsync(card, owner, agent, ctx));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.1 ETB loot — "draw a card, then discard a card." Mandatory in
    /// printed order: draw first (CR 121.1, via <see cref="Fx.DrawCards"/> so
    /// the replacement bus / empty-library SBA are honoured), then discard
    /// (CR 701.8, via the <see cref="Fx.DiscardCard"/> chokepoint). The
    /// discarded card is agent-chosen when an agent is in scope
    /// (<see cref="BotIntent.Discard"/>); agent-less / off-hand picks fall
    /// back to the last card in hand.
    /// </summary>
    private static async ValueTask ResolveLootAsync(Creature card, Player owner, IPlayerAgent? agent, ResolutionContext ctx)
    {
        var controller = card.Controller ?? owner;
        agent = ctx.Agent ?? agent ?? AgentRegistry.Get(controller);

        // "Draw a card." CR 121.1 — routed through Fx so replacements
        // (Dredge) and the empty-library tried-to-draw flag are honoured.
        Fx.DrawCards(controller, 1);

        // "then discard a card." CR 701.8 — mandatory. Operates on the hand
        // AFTER the draw. Empty hand → nothing to discard (no-op).
        var hand = controller.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return;

        ICard? pick;
        if (agent != null)
        {
            pick = await agent.ChooseFromHandAsync(controller, hand, BotIntent.Discard)
                .ConfigureAwait(false);
            if (pick == null || pick.Zone != ZoneType.Hand)
                pick = hand[^1];
        }
        else
        {
            // Pre-agent default: deterministic last-card-in-hand pick (same
            // posture as Scrapwork Mutt / Faithless Looting).
            pick = hand[^1];
        }

        // Funnel through the central discard chokepoint so DiscardedEvent
        // fires (madness / discard-matters triggers observe it).
        Fx.DiscardCard(controller, pick, wasCost: false);
    }
}
