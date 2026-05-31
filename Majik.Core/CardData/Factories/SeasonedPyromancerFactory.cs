using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seasoned Pyromancer (Modern Horizons, {1}{R}{R}).
///
/// Creature — Human Shaman 2/2. Oracle text:
///   "When this creature enters, discard two cards, then draw two cards.
///    For each nonland card discarded this way, create a 1/1 red Elemental
///    creature token.
///    {3}{R}{R}, Exile this card from your graveyard: Create two 1/1 red
///    Elemental creature tokens. Activate only as a sorcery."
///
/// ## Implementation
///
/// - 2/2 Creature — Human Shaman, mana cost {1}{R}{R}. Mana value 3.
///
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b>:
///   "When this creature enters, discard two cards, then draw two cards.
///    For each nonland card discarded this way, create a 1/1 red Elemental
///    creature token."
///   The discard-then-draw body mirrors <see cref="CatharticReunionFactory"/>
///   (discard N then draw N) with the same agent-or-fallback discard policy.
///   After discarding, each discarded card is checked for the Land card type
///   (CR 205.2a / CR 305.1) — only nonland discards create a token.
///   Token creation mirrors <see cref="YoungPyromancerFactory.CreateElementalToken"/>.
///   Active only from the battlefield (CR 603.6a).
///
/// - <b>Graveyard-activated ability (CR 113.6 / 117.1a / 307.5)</b>:
///   "{3}{R}{R}, Exile this card from your graveyard: Create two 1/1 red
///    Elemental creature tokens. Activate only as a sorcery."
///   Mirrors <see cref="SqueeDubiousMonarchFactory"/>'s graveyard-activated
///   shape. The mana cost {3}{R}{R} is exposed as a <see cref="ManaCostCost"/>
///   on the <see cref="ActivatedAbility"/>. The "exile this card from your
///   graveyard" cost is performed inside the resolve closure (graveyard zone
///   check + self-exile). The ability is sorcery-speed (CR 307.5) via the
///   <see cref="ActivatedAbility.IsSorcerySpeed"/> flag.
///   <para>
///   Zone guard: the closure short-circuits if the card is not currently in
///   the graveyard — same pattern as Squee. The engine doesn't presently
///   gate activated abilities on source zone (CR 113.6 deferred) so the
///   guard enforces correctness at resolve time.
///   </para>
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. ETB trigger is attached
///   but not registered with a <see cref="TriggerManager"/>. Suitable for
///   dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully wired.
///   ETB trigger registered with <paramref name="triggers"/> so
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s on the bus
///   route it to the stack.
///
/// ## Deferred (v1 gaps)
/// - Agent-driven discard pick prompt (currently last-2-in-hand /
///   heuristic-bot's highest-MV picker via ChooseFromHandAsync).
/// - Zone-scoped activated ability gate (CR 113.6): the graveyard ability is
///   enumerable from any zone; the resolve closure enforces the zone guard.
/// </summary>
[CardName("Seasoned Pyromancer")]
public static class SeasonedPyromancerFactory
{
    public const string CardName = "Seasoned Pyromancer";
    public const string PrintedManaCost = "{1}{R}{R}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int EtbDiscardCount = 2;
    public const int EtbDrawCount = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>CR 307.5 — graveyard activation mana cost: {3}{R}{R}.</summary>
    public const string GraveyardActivationCost = "{3}{R}{R}";

    /// <summary>Number of 1/1 Elemental tokens the graveyard ability creates.</summary>
    public const int GraveyardTokenCount = 2;

    /// <summary>
    /// Construct Seasoned Pyromancer with no live bus / trigger-manager
    /// wiring. The ETB trigger is attached for shape inspection but is
    /// not registered with any <see cref="TriggerManager"/>. Suitable
    /// for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Seasoned Pyromancer with optional event bus and trigger
    /// manager. When <paramref name="triggers"/> is supplied, the ETB
    /// trigger is registered so
    /// <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s
    /// published on the bus route it to the stack.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, discard two cards, then draw two
        //    cards. For each nonland card discarded this way, create a 1/1
        //    red Elemental creature token."
        //
        // Discard-then-draw body mirrors CatharticReunionFactory's resolve
        // effect (same agent-or-fallback policy). After discarding, each
        // discarded card's type line is checked for Land (CR 305.1 /
        // CR 205.2a) — only nonland discards trigger a token.
        // Token creation mirrors YoungPyromancerFactory.CreateElementalToken.
        // --------------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: discard two, draw two, create Elemental token per nonland discarded",
            ctx => ResolveEtbTriggerAsync(card, owner, zoneService, ctx));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // --------------------------------------------------------------------
        // Graveyard-activated ability (CR 113.6 / 117.1a / 307.5).
        //   "{3}{R}{R}, Exile this card from your graveyard: Create two
        //    1/1 red Elemental creature tokens. Activate only as a sorcery."
        //
        // Mirrors SqueeDubiousMonarchFactory's graveyard-activated shape.
        // Mana cost is exposed as a ManaCostCost on the ActivatedAbility;
        // the "exile this card from your graveyard" cost is inlined inside
        // the resolve closure. The ability is sorcery-speed (IsSorcerySpeed).
        //
        // Zone guard: short-circuit if the card is not in the graveyard at
        // resolve time (same posture as Squee). The cost was already paid
        // (mana) by the cost layer but the body is a no-op.
        // --------------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: exile self from graveyard, create two 1/1 red Elemental tokens",
            () => ResolveGraveyardActivation(card, owner, zoneService));

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(GraveyardActivationCost) },
            effects: new IEffect[] { activatedEffect },
            sorcerySpeed: true);

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.6 — create one 1/1 red Elemental creature token under
    /// <paramref name="controller"/>'s control.
    /// Reuses the same token spec as <see cref="YoungPyromancerFactory"/>.
    /// </summary>
    public static Creature CreateElementalToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Elemental",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Elemental },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 red Elemental creature token".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }

    // --- ETB body (discard two, draw two, token per nonland discarded) ----
    private static async ValueTask ResolveEtbTriggerAsync(Creature card, Player owner, ZoneService? zoneService, ResolutionContext ctx)
    {
        var controller = card.Controller ?? owner;
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);

        var discarded = await DiscardNAsync(controller, agent, EtbDiscardCount).ConfigureAwait(false);
        DrawN(controller, EtbDrawCount);

        // CR 305.1 / CR 205.2a — only nonland discards create a token.
        foreach (var discardedCard in discarded)
        {
            if (!discardedCard.HasType(CardType.Land))
            {
                CreateElementalToken(controller, zoneService);
            }
        }
    }

    private static async ValueTask<List<ICard>> DiscardNAsync(Player controller, IPlayerAgent? agent, int count)
    {
        // CR 701.16. Same agent-or-fallback policy as CatharticReunion.
        var discarded = new List<ICard>(count);
        for (var i = 0; i < count; i++)
        {
            var hand = controller.Zones.Hand.GetCards().ToList();
            if (hand.Count == 0) break;

            var pick = await PickDiscardAsync(agent, controller, hand).ConfigureAwait(false);

            controller.Zones.Hand.RemoveCard(pick);
            controller.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
            discarded.Add(pick);
        }
        return discarded;
    }

    private static async ValueTask<ICard> PickDiscardAsync(IPlayerAgent? agent, Player controller, List<ICard> hand)
    {
        if (agent == null) return hand[^1];
        var pick = await agent.ChooseFromHandAsync(controller, hand, BotIntent.Discard)
            .ConfigureAwait(false);
        if (pick == null || pick.Zone != ZoneType.Hand) return hand[^1];
        return pick;
    }

    private static void DrawN(Player controller, int count)
    {
        // CR 121.1. Empty library: stamp the SBA loss flag (CR 704.5b)
        // and short-circuit remaining draws.
        for (var i = 0; i < count; i++)
        {
            var top = controller.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                controller.MarkTriedToDrawFromEmptyLibrary();
                break;
            }
            controller.Zones.Library.RemoveCard(top);
            controller.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }

    // --- Graveyard-activated body (CR 113.6 / 307.5) ----------------------
    private static void ResolveGraveyardActivation(Creature card, Player owner, ZoneService? zoneService)
    {
        // Zone guard — only payable from graveyard.
        if (card.Zone != ZoneType.Graveyard) return;
        if (card.Owner == null) return;

        var cardOwner = card.Owner;

        // Exile this card from the graveyard as part of the cost.
        cardOwner.Zones.Graveyard.RemoveCard(card);
        cardOwner.Zones.Exile.AddCard(card);
        card.SetZone(ZoneType.Exile);

        // Create two 1/1 red Elemental creature tokens.
        var controller = card.Controller ?? owner;
        for (var i = 0; i < GraveyardTokenCount; i++)
        {
            CreateElementalToken(controller, zoneService);
        }
    }
}
