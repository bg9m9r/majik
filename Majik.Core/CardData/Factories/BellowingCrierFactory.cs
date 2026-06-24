using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bellowing Crier (Murders at Karlov Manor,
/// Creature — Frog Advisor {1}{U}).
///
/// Oracle text (Scryfall verified):
///   "When this creature enters, draw a card, then discard a card."
///
/// ## Base shape
/// Name / Creature / Frog Advisor / {1}{U} / 2/1 are materialised from the
/// embedded JSON definition (<c>bellowing-crier.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="ScrapworkMuttFactory"/>. The single printed behaviour (ETB
/// mandatory loot) is layered on here because the JSON ability schema doesn't
/// yet express it.
///
/// ## Implemented (v1)
/// - <b>2/1 Creature — Frog Advisor</b>, mana cost {1}{U}, blue (derived from
///   the {U} pip per CR 202.2c), mana value 2 (CR 202.3), owner / controller
///   wired.
/// - <b>ETB loot (CR 603.1 / CR 121.1 draw / CR 701.16 discard)</b>: "When
///   this creature enters, draw a card, then discard a card." A
///   <see cref="TriggeredAbility"/> keyed on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. Unlike Scrapwork Mutt this
///   loot is MANDATORY (no "you may") and ORDERED — draw happens FIRST, then a
///   discard. The draw is unconditional (CR 121.1); empty library mid-draw
///   flags the SBA loss (CR 704.5b) but the discard still applies to whatever
///   remains in hand. The discard ("then discard a card") is mandatory while a
///   card is available; CR 701.16a — if the hand is empty after the draw (only
///   possible when the draw failed on an empty library), nothing is discarded.
/// - Discard pick uses the same agent-or-fallback policy as
///   <see cref="ScrapworkMuttFactory"/> / <see cref="FaithlessLootingFactory"/>:
///   <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///   <see cref="BotIntent.Discard"/>, last-card-in-hand fallback.
///
/// ## Deferred (v1 gaps — shared with the other looter factories)
/// - <b>Discard-pick prompt UI</b>: v1 is agent-driven when supplied, else
///   last-card-in-hand — same gap as Faithless Looting / Scrapwork Mutt.
/// </summary>
[CardName("Bellowing Crier")]
public static class BellowingCrierFactory
{
    public const string CardName = "Bellowing Crier";
    public const string Slug = "bellowing-crier";

    /// <summary>
    /// Construct Bellowing Crier with no live runtime services. The ETB loot
    /// trigger is attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>). Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, agent: null);

    /// <summary>
    /// Construct a fully-wired Bellowing Crier. When <paramref name="triggers"/>
    /// is supplied the ETB loot trigger is registered on the bus. When
    /// <paramref name="agent"/> is supplied it drives the discard pick.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Frog
        // Advisor, {1}{U}, 2/1). No abilities in the JSON — the printed ETB
        // loot is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "When this creature enters, draw a card, then discard a card."
        // CR 603.1 self-ETB trigger. Mandatory, ordered: draw (CR 121.1)
        // FIRST, then discard (CR 701.16).
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
    /// CR 603.1 ETB loot — "draw a card, then discard a card." Draw is
    /// mandatory and resolves FIRST (CR 121.1; empty library flags the SBA loss
    /// per CR 704.5b). The discard is mandatory while a card remains; the pick
    /// mirrors Scrapwork Mutt / Faithless Looting (agent
    /// <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
    /// <see cref="BotIntent.Discard"/>, last-card fallback). CR 701.16a — if
    /// the hand is empty after the draw (only when the draw failed on an empty
    /// library), nothing is discarded.
    /// </summary>
    private static async ValueTask ResolveLootAsync(Creature card, Player owner, IPlayerAgent? agent, ResolutionContext ctx)
    {
        var controller = card.Controller ?? owner;
        agent = ctx.Agent ?? agent ?? AgentRegistry.Get(controller);

        // "draw a card." CR 121.1 — unconditional. Empty library mid-draw
        // flags the SBA loss (CR 704.5b) and there is then nothing newly
        // drawn, but the discard below still applies to the remaining hand.
        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            controller.MarkTriedToDrawFromEmptyLibrary();
        }
        else
        {
            controller.Zones.Library.RemoveCard(top);
            controller.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }

        // "then discard a card." CR 701.16 — mandatory while a card is
        // available. CR 701.16a — discard up to one when the hand is empty
        // (no-op). Agent path: ChooseFromHandAsync with BotIntent.Discard;
        // null / off-hand pick falls back to the last card in hand.
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
            pick = hand[^1];
        }

        controller.Zones.Hand.RemoveCard(pick);
        controller.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }
}
