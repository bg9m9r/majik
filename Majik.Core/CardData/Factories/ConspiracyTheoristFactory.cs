using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conspiracy Theorist (Streets of New Capenna,
/// {1}{R}). Creature — Human Shaman 2/2. Oracle text (verified against
/// Scryfall 2026-06-14):
///   "Whenever this creature attacks, you may pay {1} and discard a card.
///    If you do, draw a card.
///    Whenever you discard one or more nonland cards, you may exile one of
///    them from your graveyard. If you do, you may cast it this turn."
///
/// The base shape (name, Creature, Human + Shaman subtypes, {1}{R}, 2/2) is
/// materialised from the embedded JSON definition
/// (<c>conspiracy-theorist.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two triggered abilities are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// attack triggers, discard payments, discard-payoff exile, or impulse
/// exile-cast, so they live in the factory (same posture as
/// <see cref="IntiSeneschalOfTheSunFactory"/> + <see cref="ContainmentConstructFactory"/>,
/// whose primitives this card reuses directly).
///
/// ## Implemented (v1)
///
/// - <b>Attacks loot trigger (CR 508.1f / 603.1)</b> — fires on
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/> for
///   THIS creature (<see cref="Triggers.OnAttackSelf"/>; "Whenever this
///   creature attacks"). On resolve the controller "may pay {1} and discard a
///   card" (an optional cost — CR 603.6c "may"). When accepted, the discard
///   is routed through the central discard chokepoint
///   <see cref="Fx.DiscardCard"/> (so a <see cref="DiscardedEvent"/> publishes
///   and Conspiracy Theorist's OWN second ability — and any other "whenever
///   you discard" payoff — observes it, exactly as in real MTG); then the
///   controller draws a card (CR 121.3 via <see cref="Fx.DrawCards"/>). The
///   {1} mana payment is the agent/host's responsibility on the live priority
///   loop (same v1 optional-cost posture as Inti's "you may discard a card"
///   attack trigger — the engine has no inline cost-payment surface inside a
///   resolving triggered ability yet); the <paramref name="payAndDiscard"/>
///   chooser stands in for the "may pay" decision and the {1} is modelled as
///   paid when the chooser accepts. Declining is a clean no-op (no discard,
///   no draw).
///
/// - <b>Discard-payoff trigger (CR 603.1)</b> — "Whenever you discard one or
///   more nonland cards, you may exile one of them from your graveyard. If you
///   do, you may cast it this turn." Wired over the dedicated
///   <see cref="DiscardedEvent"/> surface (CR 701.8) via
///   <see cref="Triggers.OnDiscard"/>, gated to NONLAND cards (CR 109.3 / 305).
///   On resolve the controller "may exile" the just-discarded card from their
///   graveyard (agent-driven via <see cref="IPlayerAgent.ChooseYesNoAsync"/>;
///   default auto-accept so tests with no agent take the upside branch — same
///   posture as Containment Construct). On accept the card moves Graveyard →
///   Exile and is stamped with <see cref="Card.GrantRuntimeExileCast"/> for
///   the controller at its printed mana cost ("you may cast it this turn",
///   CR 118.9 — the same impulse exile-cast primitive Containment Construct /
///   Ragavan / Light Up the Stage use). The grant clears on the first
///   <see cref="StepStateType.Cleanup"/> step seen after the discard (CR 514.2
///   — end of the current turn, matching the printed "this turn" duration).
///
/// ## Deferred (v1 gaps)
///
/// - <b>"One or more nonland cards" batching</b>: the discard-payoff trigger
///   fires per <see cref="DiscardedEvent"/> (one per discarded card). A
///   multi-card discard therefore fires the trigger once per nonland card
///   discarded rather than once for the batch with a choose-one-of-them step.
///   In v1 the per-card pick coincides with "exile one of them" for the
///   common single-card discard (including Conspiracy Theorist's own loot,
///   which discards exactly one). Same batching posture as
///   <see cref="IntiSeneschalOfTheSunFactory"/> / Containment Construct.
/// - <b>Inline {1} payment for the loot</b>: the "pay {1}" half of the attack
///   trigger's optional cost is not deducted inside the resolving ability
///   (the engine has no in-resolution cost-payment surface yet — same gap as
///   Inti). The host pays it on the priority loop; the chooser models the
///   "may pay" decision.
/// - <b>Agent-driven discard / exile pick</b>: the loot discards the FIRST
///   card in hand when accepted; the payoff exiles the just-discarded card.
///   Full agent-driven selection is deferred behind the same queue as
///   Faithless Looting / Liliana of the Veil.
///
/// CR rule references: 205.3m (Human/Shaman subtypes), 508.1f (attacks
/// trigger), 603.1 / 603.6c (triggered ability / "may"), 701.8 (discard),
/// 118.9 (impulse cast permission), 514.2 ("this turn" cleanup), 109.3 / 305
/// (nonland card).
/// </summary>
[CardName("Conspiracy Theorist")]
public static class ConspiracyTheoristFactory
{
    public const string CardName = "Conspiracy Theorist";
    public const string Slug = "conspiracy-theorist";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Conspiracy Theorist with no live wiring (the shape /
    /// dispatcher path). Both triggers are attached for shape observability
    /// but not registered; the discard payoff's impulse grant will not
    /// auto-clear without an event bus. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, payAndDiscard: null);

    /// <summary>
    /// Construct Conspiracy Theorist with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the discard-payoff trigger's
    /// impulse grant clears on the first <see cref="StepStateType.Cleanup"/>
    /// step seen after the discard (CR 514.2 — "this turn").</param>
    /// <param name="triggers">TriggerManager the attack + discard triggers are
    /// registered with so they surface as pending. May be null.</param>
    /// <param name="payAndDiscard">"You may pay {1} and discard a card"
    /// chooser for the attack trigger. Returns true to pay + discard (the
    /// first card in hand), false to decline. May be null — defaults to
    /// paying + discarding whenever the controller has a card in hand (the
    /// upside branch).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<bool>? payAndDiscard = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        AddAttackLootTrigger(card, owner, eventBus, payAndDiscard, triggers);
        AddDiscardPayoffTrigger(card, owner, eventBus, triggers);

        return card;
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever this creature attacks, you may pay {1} and
    // discard a card. If you do, draw a card." (CR 508.1f / 603.1.)
    // -----------------------------------------------------------------------
    private static void AddAttackLootTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        Func<bool>? payAndDiscard,
        TriggerManager? triggers)
    {
        var lootEffect = new Effect(
            $"{CardName}: on attack, you may pay {{1}} and discard a card; if you do, draw a card",
            () => ResolveAttackLoot(card, owner, eventBus, payAndDiscard));

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            // CR 508.1f — "Whenever this creature attacks" fires on this
            // creature's own attack declaration.
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { lootEffect },
            // CR 113.6 — the ability functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);
    }

    private static void ResolveAttackLoot(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        Func<bool>? payAndDiscard)
    {
        var controller = card.Controller ?? owner;

        // "You may pay {1} and discard a card." CR 603.6c — optional. Default:
        // pay + discard whenever the controller has a card in hand to discard
        // (the upside branch). The {1} payment itself is the host's
        // responsibility on the priority loop (same gap as Inti's optional
        // discard cost); the chooser models the accept/decline decision.
        var wants = payAndDiscard?.Invoke()
            ?? controller.Zones.Hand.GetCards().Any();
        if (!wants) return;

        var toDiscard = controller.Zones.Hand.GetCards().FirstOrDefault();
        if (toDiscard == null) return; // can't pay the optional cost — no card.

        // Route through the central discard chokepoint (CR 701.8) so a
        // DiscardedEvent publishes and the discard-payoff trigger below (and
        // any other "whenever you discard" payoff) observes it. wasCost: true
        // — the discard is paid as part of the trigger's cost.
        Fx.DiscardCard(controller, toDiscard, wasCost: true, eventBus);

        // "If you do, draw a card." CR 121.3.
        Fx.DrawCards(controller, 1);
    }

    // -----------------------------------------------------------------------
    // Discard-payoff trigger — "Whenever you discard one or more nonland
    // cards, you may exile one of them from your graveyard. If you do, you may
    // cast it this turn." (CR 603.1; dedicated DiscardedEvent surface.)
    // -----------------------------------------------------------------------
    private static void AddDiscardPayoffTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ICard? capturedDiscard = null;

        // "Whenever you discard ... nonland cards" — the dedicated discard
        // surface (CR 701.8), gated to the controller (CR 109.5) + nonland
        // cards (CR 109.3 / 305). Capture the just-discarded card so the
        // resolve body can exile that specific card (CR 603.2).
        var baseCondition = Triggers.OnDiscard(card.Controller ?? owner);
        var condition = new EventTriggerCondition<DiscardedEvent>((e, ability) =>
        {
            // "You discard" — CR 109.5, delegated to the shared OnDiscard
            // condition (gates to the controller).
            if (!baseCondition.Matches(e, ability)) return false;
            // "Nonland card" — CR 305.7: a card whose types include Land is a
            // land and does NOT fire the trigger (artifact lands etc.).
            if (e.Card.HasType(CardType.Land)) return false;
            capturedDiscard = e.Card;
            return true;
        });

        var exileEffect = new Effect(
            $"{CardName}: may exile a discarded nonland card from your graveyard; if you do, you may cast it this turn",
            async ctx =>
            {
                var discarded = capturedDiscard;
                capturedDiscard = null;
                await ResolveDiscardPayoffAsync(discarded, card, owner, eventBus, ctx)
                    .ConfigureAwait(false);
            });

        var discardTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { exileEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(discardTrigger);
        triggers?.RegisterTriggeredAbility(discardTrigger);
    }

    private static async ValueTask ResolveDiscardPayoffAsync(
        ICard? discarded,
        Creature card,
        Player owner,
        IEventBus? eventBus,
        ResolutionContext ctx)
    {
        if (discarded == null) return;

        var controller = card.Controller ?? owner;
        if (!await ShouldExileAsync(discarded, controller, ctx).ConfigureAwait(false)) return;

        if (!MoveDiscardToExile(discarded)) return;

        // "If you do, you may cast it this turn." CR 118.9 runtime grant.
        if (discarded is not Card stampable) return;
        stampable.GrantRuntimeExileCast(controller, stampable.ManaCostValue);

        // "This turn" — CR 514.2. Clear on the next Cleanup step.
        ScheduleEndOfTurnGrantClear(stampable, controller, eventBus);
    }

    private static async ValueTask<bool> ShouldExileAsync(
        ICard discarded, Player controller, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        // CR 603.1 may-clause. Default auto-accept (BotIntent.CardAdvantage) so
        // tests with no agent registered take the upside branch.
        if (agent == null) return true;
        return await agent.ChooseYesNoAsync(
            $"Exile {discarded.Name} from {controller.Name}'s graveyard to cast it this turn?",
            BotIntent.CardAdvantage).ConfigureAwait(false);
    }

    private static bool MoveDiscardToExile(ICard discarded)
    {
        // Guard the zone in case a sibling effect already moved the card
        // (Rest in Peace, Leyline of the Void).
        if (discarded.Zone != ZoneType.Graveyard) return false;
        var graveyardOwner = discarded.Owner;
        if (graveyardOwner == null) return false;
        if (!graveyardOwner.Zones.Graveyard.GetCards().Contains(discarded)) return false;

        graveyardOwner.Zones.Graveyard.RemoveCard(discarded);
        graveyardOwner.Zones.Exile.AddCard(discarded);
        discarded.SetZone(ZoneType.Exile);
        return true;
    }

    private static void ScheduleEndOfTurnGrantClear(
        Card stampable,
        Player controller,
        IEventBus? eventBus)
    {
        if (eventBus == null) return;

        // The discard can happen on any player's turn, so the cleanup we wait
        // for is the next one regardless of who is active. Only revoke if the
        // stamp is still the live grant — a re-stamp by a later effect
        // overwrites and we must not clobber it.
        Action<StepStartedEvent>? handler = null;
        handler = (e) =>
        {
            if (e.StepType != StepStateType.Cleanup) return;
            if (ReferenceEquals(stampable.RuntimeExileCastAllowedCaster, controller))
            {
                stampable.ClearRuntimeExileCast();
            }
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }
}
