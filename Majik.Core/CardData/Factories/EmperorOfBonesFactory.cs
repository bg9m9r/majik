using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Emperor of Bones (Modern Horizons 3,
/// <c>{1}{B}</c>). Creature — Skeleton Noble. 2/2.
///
/// Oracle text (Scryfall-verified):
/// <list type="number">
///   <item>"At the beginning of combat on your turn, exile up to one
///       target card from a graveyard."</item>
///   <item>"<c>{1}{B}</c>: Adapt 2."</item>
///   <item>"Whenever one or more +1/+1 counters are put on this creature,
///       put a creature card exiled with this creature onto the
///       battlefield under your control with a finality counter on it.
///       It gains haste. Sacrifice it at the beginning of the next end
///       step."</item>
/// </list>
///
/// ## Implementation
/// <list type="bullet">
///   <item><b>Ability 1 — begin-of-combat exile-and-track</b>: wired as
///   <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnStepBegin"/>(<paramref name="owner"/>,
///   <see cref="PhaseStateType.BeginningOfCombat"/>) — only fires on the
///   controller's own combat steps (CR 500.4). Resolution honours a
///   pre-chosen target on the trigger, or v1 auto-picks the first card
///   in any graveyard (deterministic; mirrors Soul-Guide Lantern). The
///   exiled card is moved to its owner's exile zone AND recorded in the
///   per-Emperor <see cref="EmperorOfBonesState.ExiledWith"/> ledger so
///   ability 3 can find it. Activated via <see cref="GetState"/>.</item>
///
///   <item><b>Ability 2 — <c>{1}{B}</c>: Adapt 2</b>: delegates to
///   <see cref="AdaptFactory.Build"/> with cost <c>{1}{B}</c>, N=2. The
///   helper handles the CR 702.116b "no +1/+1 counters" gate and routes
///   the placement through <see cref="CountersService.Add"/> so the
///   post-commit <see cref="CounterAddedEvent"/> publishes (which
///   ability 3 subscribes to).</item>
///
///   <item><b>Ability 3 — +1/+1-counter return trigger</b>: subscribes
///   to <see cref="CounterAddedEvent"/> matching this Emperor +
///   <see cref="CounterType.PlusOnePlusOne"/>. Resolution picks the
///   first creature card in the Emperor's <c>ExiledWith</c> ledger,
///   moves it from exile → battlefield under the Emperor's controller,
///   adds a <see cref="CounterType.Finality"/> counter (gating the
///   global <see cref="FinalityCounterReplacement"/> die-redirect),
///   grants haste via <see cref="GrantKeywordUntilEndOfTurnEffect"/>,
///   and registers a one-shot <see cref="DelayedTriggeredAbility"/> for
///   the "sacrifice at next end step" rider (CR 603.7). When the ledger
///   has no creature card, the trigger is a silent no-op.</item>
/// </list>
///
/// <para>
/// <b>Finality counter wiring</b>: the factory eagerly calls
/// <see cref="FinalityCounterReplacement.Register"/> on the supplied
/// <see cref="ReplacementBus"/> so the die-redirect is in place for any
/// finality-marked permanent the Emperor produces. Idempotent across
/// multiple Emperor instances on the same bus.
/// </para>
///
/// <para>
/// <b>Deferred (v2)</b>: the begin-combat trigger does not yet expose
/// an agent prompt for the optional target ("up to one") — when no
/// pre-chosen target is on the trigger and no live graveyards have
/// cards, the trigger is a clean no-op. v2 wires the agent prompt
/// through the standard <see cref="TargetRequest"/> pipeline.
/// </para>
/// </summary>
[CardName("Emperor of Bones")]
public static class EmperorOfBonesFactory
{
    public const string CardName = "Emperor of Bones";
    public const string PrintedManaCost = "{1}{B}";
    public const string AdaptCost = "{1}{B}";
    public const int AdaptAmount = 2;
    public const int BasePower = 2;
    public const int BaseToughness = 2;

    /// <summary>
    /// Per-Emperor state: which exile-resident cards were exiled by this
    /// Emperor's begin-combat trigger (ability 1). Used by ability 3 to
    /// pick a creature card to return on the +1/+1-counter trigger.
    /// Keyed off the Emperor card instance via
    /// <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/>
    /// so multiple Emperors in the same game keep separate ledgers
    /// (mirrors <see cref="DauthiVoidwalkerFactory"/>).
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Card, EmperorOfBonesState>
        _state = new();

    /// <summary>
    /// Retrieve the <see cref="EmperorOfBonesState"/> attached to an
    /// Emperor instance produced by this factory. Returns null when the
    /// card was not built by this factory.
    /// </summary>
    public static EmperorOfBonesState? GetState(Card emperor)
    {
        ArgumentNullException.ThrowIfNull(emperor);
        return _state.TryGetValue(emperor, out var s) ? s : null;
    }

    /// <summary>
    /// Construct Emperor of Bones for the dispatcher / shape-test path:
    /// no <see cref="TriggerManager"/>, <see cref="ZoneService"/>,
    /// <see cref="ReplacementBus"/>, <see cref="IEventBus"/>, or
    /// <see cref="IPlayerAgent"/> wired. Identity + ability shape are
    /// fully populated; live bus-driven trigger firing is a no-op.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null,
            replacements: null, eventBus: null, agent: null);

    /// <summary>
    /// Construct Emperor of Bones with optional engine plumbing. Each
    /// dependency is independent:
    /// <list type="bullet">
    ///   <item><paramref name="triggers"/> registers the begin-combat
    ///   trigger so the trigger bus surfaces it on
    ///   <see cref="StepStartedEvent"/>.</item>
    ///   <item><paramref name="zoneService"/> routes exile → battlefield
    ///   for ability 3's return, and battlefield → graveyard for
    ///   ability 3's delayed sac, through the replacement bus so the
    ///   finality counter's die-redirect actually fires on the sac.</item>
    ///   <item><paramref name="replacements"/> is where
    ///   <see cref="FinalityCounterReplacement"/> registers and where
    ///   the Adapt counter-placement / return-side counter-placement
    ///   route for Hardened Scales etc. bumps.</item>
    ///   <item><paramref name="eventBus"/> publishes the
    ///   <see cref="CounterAddedEvent"/> that ability 3 listens for.</item>
    ///   <item><paramref name="agent"/> may surface the begin-combat
    ///   target prompt in v2; v1 ignores it and uses the deterministic
    ///   auto-target.</item>
    /// </list>
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService,
        ReplacementBus? replacements,
        IEventBus? eventBus,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: BasePower,
            toughness: BaseToughness,
            subtypes: new[] { CardSubtype.Skeleton, CardSubtype.Noble });

        card.SetOwner(owner);
        card.SetController(owner);

        var state = new EmperorOfBonesState();
        _state.AddOrUpdate(card, state);

        // ----------------------------------------------------------------
        // Finality counter infrastructure — register the global die-
        // redirect on the supplied bus (idempotent). Without this, the
        // counter ability 3 stamps does nothing.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            FinalityCounterReplacement.Register(replacements);
        }

        // ----------------------------------------------------------------
        // Ability 1 — "At the beginning of combat on your turn, exile up
        // to one target card from a graveyard." CR 603.6 / CR 500.4.
        // ----------------------------------------------------------------
        TriggeredAbility? combatTrigger = null;
        var combatEffect = new Effect(
            $"{CardName}: exile up to one target card from a graveyard",
            () => ResolveBeginCombatExile(card, owner, state, combatTrigger));

        combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.BeginningOfCombat),
            effects: new IEffect[] { combatEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to one target card in a graveyard",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate | BotIntent.CardAdvantage),
            });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // ----------------------------------------------------------------
        // Ability 2 — "{1}{B}: Adapt 2." Delegate to AdaptFactory (CR
        // 702.116). AdaptFactory.Build stamps the keyword marker and
        // returns the activated ability; we mount it on the source.
        // ----------------------------------------------------------------
        var adaptAbility = AdaptFactory.Build(
            card, AdaptCost, AdaptAmount, replacements, eventBus);
        card.AddAbility(adaptAbility);

        // ----------------------------------------------------------------
        // Ability 3 — "Whenever one or more +1/+1 counters are put on
        // this creature, put a creature card exiled with this creature
        // onto the battlefield under your control with a finality
        // counter on it. It gains haste. Sacrifice it at the beginning
        // of the next end step."
        //
        // Wired as a TriggeredAbility over a self-scoped
        // CounterAddedEvent — fires on every CountersService.Add call
        // targeting this Emperor with +1/+1 counters.
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return creature card exiled with this creature " +
            "(finality counter + haste + delayed sac next end step)",
            () => ResolveCounterReturn(
                card, owner, state, zoneService, triggers, replacements, eventBus));

        var counterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CounterAddedEvent>(
                (e, _) => ReferenceEquals(e.Target, card)
                          && e.CounterType == CounterType.PlusOnePlusOne),
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(counterTrigger);
        triggers?.RegisterTriggeredAbility(counterTrigger);

        return card;
    }

    // ------------------------------------------------------------------------
    // Resolution helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// Resolve ability 1's begin-of-combat exile. Pick a target card from
    /// the trigger's chosen-targets list, or v1 auto-pick the first card
    /// in any graveyard (deterministic; mirrors Soul-Guide Lantern). The
    /// chosen card is moved to its owner's exile zone and recorded in
    /// the per-Emperor ledger so ability 3 can find it.
    /// </summary>
    private static void ResolveBeginCombatExile(
        Card emperor, Player controller,
        EmperorOfBonesState state, TriggeredAbility? trigger)
    {
        // Emperor must still be on the battlefield to fire ability 1.
        if (emperor.Zone != ZoneType.Battlefield) return;

        ICard? target = null;
        if (trigger != null
            && trigger.ChosenTargets.Count > 0
            && trigger.ChosenTargets[0].Count > 0
            && trigger.ChosenTargets[0][0] is ICard chosen)
        {
            target = chosen;
        }
        else
        {
            // v1 deterministic fallback — first card in the controller's
            // own graveyard (then any other graveyard). "Up to one" means
            // an empty graveyard is a clean no-op (no auto-target forced).
            target = controller.Zones.Graveyard.GetCards().FirstOrDefault();
        }

        if (target == null) return;

        // CR 608.2b — the target card must still be in a graveyard at
        // resolution time.
        if (target.Zone != ZoneType.Graveyard) return;
        var targetOwner = target.Owner;
        if (targetOwner == null) return;

        targetOwner.Zones.Graveyard.RemoveCard(target);
        targetOwner.Zones.Exile.AddCard(target);
        target.SetZone(ZoneType.Exile);

        // Per-Emperor "exiled with this creature" ledger (CR 400.7 — the
        // exile zone's "exiled with" relationship is bookkeeping the
        // game tracks for return effects).
        state.AddExiledWith(target);
    }

    /// <summary>
    /// Resolve ability 3's +1/+1-counter return: pick the first creature
    /// card in the ledger, move it from exile → battlefield under the
    /// Emperor's controller, stamp a finality counter, grant haste, and
    /// register a one-shot delayed end-step sacrifice trigger.
    /// </summary>
    private static void ResolveCounterReturn(
        Creature emperor, Player controller, EmperorOfBonesState state,
        ZoneService? zoneService, TriggerManager? triggers,
        ReplacementBus? replacements, IEventBus? eventBus)
    {
        // Emperor must still be on the battlefield to fire ability 3.
        if (emperor.Zone != ZoneType.Battlefield) return;

        // Pick the first creature card in the ledger. Non-creature cards
        // are skipped (the printed text restricts to "a creature card").
        var pick = state.ExiledWith
            .OfType<ICard>()
            .FirstOrDefault(c => c.HasType(CardType.Creature)
                && c.Zone == ZoneType.Exile);
        if (pick == null) return;

        // Consume from the ledger up front so a retrigger inside this
        // resolution can't double-pick the same card.
        state.RemoveExiledWith(pick);

        var pickOwner = pick.Owner ?? controller;

        // --------------------------------------------------------------
        // Exile → Battlefield under the Emperor's controller. Route
        // through ZoneService when wired so CardMovedEvent publishes
        // and any ETB triggers on the returning creature fire (CR
        // 603.6a). The raw-zone path is the shape fallback.
        // --------------------------------------------------------------
        if (zoneService != null)
        {
            zoneService.MoveCard(pick, ZoneType.Exile, ZoneType.Battlefield, controller);
        }
        else
        {
            pickOwner.Zones.Exile.RemoveCard(pick);
            controller.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(controller);
        }

        // --------------------------------------------------------------
        // "With a finality counter on it" — CR 122.1m. Route through
        // CountersService.Add so the placement honours any replacement
        // bus modifiers (none today for finality; symmetry with Persist /
        // Modular). When the card is not a Permanent (shouldn't happen
        // — the OfType<ICard> + HasType(Creature) above gate it), the
        // placement is a silent no-op.
        // --------------------------------------------------------------
        if (pick is Permanent finalityTarget)
        {
            CountersService.Add(
                finalityTarget, CounterType.Finality, 1, replacements, eventBus);
        }

        // --------------------------------------------------------------
        // "It gains haste." — CR 613.1c (Layer 6) keyword grant.
        // EOT-scoped via GrantKeywordUntilEndOfTurnEffect; since the
        // delayed sac fires at the next end step (same boundary the
        // EOT grant expires on), the EOT scope is observationally
        // equivalent to a no-duration grant for the creature's
        // lifetime (mirrors Sneak Attack).
        // --------------------------------------------------------------
        if (pick is Creature pickCreature)
        {
            if (pickCreature.ActiveEffects != null)
            {
                pickCreature.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(pickCreature, "Haste"));
            }
            // CR 702.10b — Haste lifts summoning sickness for attack
            // declaration.
            pickCreature.HasSummoningSickness = false;
        }

        // --------------------------------------------------------------
        // "Sacrifice it at the beginning of the next end step." CR 603.7
        // — one-shot delayed triggered ability. Fires on the first
        // StepStartedEvent(End) strictly after this resolution
        // (mirrors Sneak Attack / Through the Breach).
        // --------------------------------------------------------------
        if (triggers != null)
        {
            var resolvedAt = DateTime.UtcNow;
            var sacEffect = new Effect(
                $"{CardName}: sacrifice {pick.Name} at next end step",
                () =>
                {
                    if (pick.Zone != ZoneType.Battlefield) return;
                    var bfPlayer = pick.Controller;
                    if (bfPlayer == null) return;
                    if (!bfPlayer.Zones.Battlefield.GetCards().Contains(pick)) return;

                    if (zoneService != null)
                    {
                        // CR 701.16 — sacrifice routes through ZoneService
                        // so the finality counter's die-redirect (registered
                        // on the same bus) rewrites Battlefield → Graveyard
                        // to Battlefield → Exile.
                        zoneService.MoveCard(
                            pick, ZoneType.Battlefield, ZoneType.Graveyard, bfPlayer);
                    }
                    else
                    {
                        bfPlayer.Zones.Battlefield.RemoveCard(pick);
                        (pick.Owner ?? bfPlayer).Zones.Graveyard.AddCard(pick);
                        pick.SetZone(ZoneType.Graveyard);
                    }
                });

            var delayed = new DelayedTriggeredAbility(
                source: emperor,
                controller: controller,
                condition: new EventTriggerCondition<StepStartedEvent>(
                    (e, _) => e.StepType == PhaseStateType.End
                              && e.Timestamp > resolvedAt),
                effects: new IEffect[] { sacEffect });

            triggers.RegisterDelayed(delayed);
        }
    }
}

/// <summary>
/// Per-Emperor "exiled with this creature" ledger. Tracks the order of
/// exile so v1 auto-pick (first-in) is deterministic.
/// </summary>
public sealed class EmperorOfBonesState
{
    private readonly List<ICard> _exiledWith = new();

    /// <summary>All cards currently exiled with this Emperor, in
    /// insertion order. Includes non-creature cards (ability 3 filters
    /// to creatures at resolution).</summary>
    public IReadOnlyList<ICard> ExiledWith => _exiledWith;

    /// <summary>Record <paramref name="card"/> as exiled with this
    /// Emperor. Idempotent.</summary>
    public void AddExiledWith(ICard card)
    {
        if (card == null) return;
        if (_exiledWith.Contains(card)) return;
        _exiledWith.Add(card);
    }

    /// <summary>Remove <paramref name="card"/> from the ledger. Returns
    /// true if the card was in the ledger.</summary>
    public bool RemoveExiledWith(ICard card)
    {
        if (card == null) return false;
        return _exiledWith.Remove(card);
    }
}
