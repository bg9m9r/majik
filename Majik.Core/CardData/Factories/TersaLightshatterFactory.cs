using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tersa Lightshatter (Tarkir: Dragonstorm, {2}{R}).
/// Legendary Creature — Orc Wizard 3/3. Oracle text (verified against Scryfall):
///   "Haste
///    When Tersa Lightshatter enters, discard up to two cards, then draw that
///    many cards.
///    Whenever Tersa Lightshatter attacks, if there are seven or more cards in
///    your graveyard, exile a card at random from your graveyard. You may play
///    that card this turn."
///
/// The base shape (name, Legendary supertype, Creature, Orc + Wizard subtypes,
/// {2}{R}, 3/3, Haste) is materialised from the embedded JSON definition
/// (<c>tersa-lightshatter.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (Haste is an intrinsic
/// <see cref="KeywordAbility"/> stamped by the JSON <c>keywords</c> array). The
/// two triggered abilities are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express the discard-then-draw ETB
/// or the random-graveyard-exile attack trigger, so they live in the factory
/// (same posture as <see cref="IntiSeneschalOfTheSunFactory"/> /
/// <see cref="SeasonedPyromancerFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b> —
///   "When Tersa Lightshatter enters, discard up to two cards, then draw that
///    many cards." The discard-then-draw body mirrors
///   <see cref="SeasonedPyromancerFactory"/>'s discard policy (agent-or-fallback
///   pick via <c>ChooseFromHandAsync</c>), but the count is "up to two" (CR
///   701.16) so it discards as many as the controller has, capped at two, and
///   then draws EXACTLY the number discarded (CR 121.1) — a land-light /
///   empty hand discards fewer and draws fewer, matching the "that many" link.
///
/// - <b>Attack triggered ability (CR 508.1 / 603.1 / 603.4)</b> —
///   "Whenever Tersa Lightshatter attacks, if there are seven or more cards in
///    your graveyard, exile a card at random from your graveyard. You may play
///    that card this turn." Fires on <see cref="AttackersDeclaredEvent"/> when
///   Tersa herself is among the declared attackers ("Tersa attacks", not
///   "you attack" — the self-attack predicate, same shape as the per-attacker
///   read in <see cref="SoaringThoughtThiefFactory"/>). The "if there are seven
///   or more cards in your graveyard" clause is an INTERVENING-IF condition (CR
///   603.4) — it gates BOTH whether the ability triggers AND is re-checked on
///   resolution, so it lives in <see cref="TriggeredAbility.InterveningIf"/>.
///   On resolve it exiles a uniformly-random card from the controller's
///   graveyard (CR 701.20; the "at random" pick uses the per-game
///   <see cref="GameRandom"/> via <see cref="GameRandomRegistry"/>, same as
///   <see cref="BurningInquiryFactory"/>) and stamps the reusable
///   "you may play that card this turn" permission
///   (<see cref="ExilePlayPermission.GrantUntil"/> with
///   <see cref="ExilePlayExpiry.EndOfTurn"/>) — which covers BOTH the
///   spell-cast half and the land-play half (CR 305.2 / 601.1), so a random
///   land is playable and a random spell castable, both bounded to this turn
///   (CR 514.2).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — canonical build (no bus). Both triggers are
///   attached for shape observability; the attack trigger's "this turn" play
///   permission persists until cleared by hand (test path).
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully wired.
///   Triggers register with <paramref name="triggers"/>; the play permission
///   the attack trigger stamps clears at the controller's next Cleanup step
///   (CR 514.2) via the supplied bus.
///
/// ## Deferred (v1 gaps)
/// - <b>"Up to two" / agent count</b>: the ETB discards as many as available
///   capped at two (the upside branch). Full agent-driven "how many to discard"
///   selection is deferred behind the same queue as Faithless Looting /
///   Cathartic Reunion (the agent still chooses WHICH cards via
///   <c>ChooseFromHandAsync</c>).
/// - <b>Empty-graveyard exile</b>: a graveyard that somehow has < 7 cards by
///   resolution makes the intervening-if false (clean no-op, CR 603.4); a
///   graveyard with ≥ 7 always has a card to exile.
/// </summary>
[CardName("Tersa Lightshatter")]
public static class TersaLightshatterFactory
{
    public const string CardName = "Tersa Lightshatter";
    public const string Slug = "tersa-lightshatter";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>ETB "discard up to two cards" cap (CR 701.16).</summary>
    public const int EtbDiscardMax = 2;

    /// <summary>The graveyard-size threshold the attack trigger gates on
    /// (CR 603.4 intervening-if): "seven or more cards in your graveyard".</summary>
    public const int GraveyardThreshold = 7;

    /// <summary>
    /// Canonical build with no live wiring (the shape / dispatcher path). Both
    /// triggers are attached for shape observability but not registered; the
    /// attack trigger's "this turn" play permission will not auto-clear without
    /// an event bus. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Tersa Lightshatter with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both triggered abilities are
    /// registered. When <paramref name="eventBus"/> is supplied, the attack
    /// trigger's exile play-permission clears at the controller's next Cleanup
    /// step (CR 514.2).
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Orc + Wizard, {2}{R}, 3/3, Haste). Haste is an intrinsic
        // KeywordAbility stamped by the JSON keywords array — no abilities in
        // the JSON's abilities[] array; both triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        AddEtbTrigger(card, owner, triggers);
        AddAttackTrigger(card, owner, eventBus, triggers);

        return card;
    }

    // -----------------------------------------------------------------------
    // ETB trigger — "When Tersa Lightshatter enters, discard up to two cards,
    // then draw that many cards." (CR 603.1 / 603.6a.)
    // -----------------------------------------------------------------------
    private static void AddEtbTrigger(Creature card, Player owner, TriggerManager? triggers)
    {
        var etbEffect = new Effect(
            $"{CardName}: discard up to two cards, then draw that many",
            ctx => ResolveEtbTriggerAsync(card, owner, ctx));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);
    }

    private static async ValueTask ResolveEtbTriggerAsync(Creature card, Player owner, ResolutionContext ctx)
    {
        var controller = card.Controller ?? owner;
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);

        // "discard up to two cards" — CR 701.16. Discard as many as available,
        // capped at two (the upside branch; the agent chooses WHICH cards).
        var discarded = 0;
        for (var i = 0; i < EtbDiscardMax; i++)
        {
            var hand = controller.Zones.Hand.GetCards().ToList();
            if (hand.Count == 0) break;

            var pick = await PickDiscardAsync(agent, controller, hand).ConfigureAwait(false);

            controller.Zones.Hand.RemoveCard(pick);
            controller.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
            discarded++;
        }

        // "then draw that many cards." — CR 121.1. Exactly the number discarded.
        DrawN(controller, discarded);
    }

    private static async ValueTask<ICard> PickDiscardAsync(IPlayerAgent? agent, Player controller, List<ICard> hand)
    {
        // Same agent-or-fallback discard policy as Seasoned Pyromancer.
        if (agent == null) return hand[^1];
        var pick = await agent.ChooseFromHandAsync(controller, hand, BotIntent.Discard)
            .ConfigureAwait(false);
        if (pick == null || pick.Zone != ZoneType.Hand) return hand[^1];
        return pick;
    }

    private static void DrawN(Player controller, int count)
    {
        // CR 121.1. Empty library: stamp the SBA loss flag (CR 704.5b) and
        // short-circuit remaining draws (same posture as Seasoned Pyromancer).
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

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever Tersa Lightshatter attacks, if there are seven
    // or more cards in your graveyard, exile a card at random from your
    // graveyard. You may play that card this turn." (CR 508.1 / 603.1 / 603.4.)
    // -----------------------------------------------------------------------
    private static void AddAttackTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        // "Whenever Tersa Lightshatter attacks" — Tersa herself must be among
        // the declared attackers (CR 508.1; the self-attack predicate, distinct
        // from "Whenever you attack"). Reads the live combat off the event.
        var condition = new EventTriggerCondition<AttackersDeclaredEvent>(
            (e, _) => IsSelfAmongAttackers(e, card));

        var attackEffect = new Effect(
            $"{CardName}: on attack (≥{GraveyardThreshold} cards in graveyard), exile a random graveyard card; you may play it this turn",
            () => ResolveAttackTrigger(card, owner, eventBus));

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { attackEffect },
            // CR 603.4 — "if there are seven or more cards in your graveyard" is
            // an intervening-if condition: gates triggering AND is re-checked on
            // resolution.
            interveningIf: () => GraveyardCount(card, owner) >= GraveyardThreshold,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);
    }

    private static bool IsSelfAmongAttackers(AttackersDeclaredEvent e, Creature card)
    {
        foreach (var atk in e.Combat.Attackers)
        {
            if (ReferenceEquals(atk?.Creature, card)) return true;
        }
        return false;
    }

    private static int GraveyardCount(Creature card, Player owner)
    {
        var controller = card.Controller ?? owner;
        return controller.Zones.Graveyard.GetCards().Count();
    }

    private static void ResolveAttackTrigger(Creature card, Player owner, IEventBus? eventBus)
    {
        var controller = card.Controller ?? owner;

        // CR 603.4 — re-check the intervening-if on resolution.
        var grave = controller.Zones.Graveyard.GetCards().OfType<Card>().ToList();
        if (grave.Count < GraveyardThreshold) return;

        // "exile a card at random from your graveyard" — CR 701.20. Uniform
        // random pick via the per-game GameRandom (same seam as Burning
        // Inquiry's "at random" discard).
        var rng = GameRandomRegistry.Get(controller);
        var pick = grave[rng.Next(grave.Count)];

        controller.Zones.Graveyard.RemoveCard(pick);
        controller.Zones.Exile.AddCard(pick);
        pick.SetZone(ZoneType.Exile);

        // "You may play that card this turn." — CR 118.9 / 514.2. The reusable
        // permission covers BOTH the spell-cast half and the exiled-land
        // land-play half (CR 305.2 / 601.1); it expires at the controller's
        // next Cleanup step when a bus is supplied (else persists — test path).
        ExilePlayPermission.GrantUntil(
            pick, controller, pick.ManaCostValue,
            ExilePlayExpiry.EndOfTurn, eventBus);
    }
}
