using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// Async driver for one full turn. Implements the simplified phase sequence:
///   1. Beginning: Untap → Upkeep (priority) → Draw (skip on turn 1)
///   2. Main 1 (priority)
///   3. Combat: BeginningOfCombat (priority) → DeclareAttackers (CombatFlow
///      handles attacker/blocker declaration + damage; SBA cleans up)
///   4. Main 2 (priority)
///   5. End: End step (priority) → Cleanup (discard to hand size, empty
///      mana pools, remove damage from creatures)
///
/// Triggers fired by phase transitions / damage are pumped through
/// <see cref="PriorityLoop"/> at each step.
/// </summary>
public sealed class TurnDriver
{
    private readonly IReadOnlyList<Player> _players;
    private readonly IReadOnlyDictionary<Player, IPlayerAgent> _agents;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zoneService;
    private readonly TriggerManager _triggerManager;
    private readonly StackResolver _stackResolver;
    private readonly StateBasedActions _sba;
    private readonly PriorityManager _priorityManager;
    private readonly CombatFlow _combatFlow;
    private readonly Majik.Core.Effects.ContinuousEffectsService? _continuousEffects;
    private readonly Majik.Core.Effects.ReplacementBus? _replacements;
    private readonly LandDropTracker _landDropTracker;
    private readonly AdditionalCombatQueue _additionalCombats = new();
    private PhaseStateType _currentPhase;
    private int _currentTurnNumber;

    /// <summary>
    /// Per-turn event tally — creatures died, permanents left, cards drawn.
    /// Reset at the start of each turn; consulted by revolt / connive / draw-watchers.
    /// </summary>
    public TurnState TurnState { get; } = new();

    /// <summary>Effects that grant the current turn an additional combat
    /// phase (Aggravated Assault, Combat Celebrant, Relentless Assault)
    /// enqueue here. The turn loop re-runs the combat sequence as long
    /// as the queue is non-empty.</summary>
    public AdditionalCombatQueue AdditionalCombats => _additionalCombats;

    private readonly Majik.Core.Events.IEventBus? _eventBus;
    private readonly Func<ICard, Player, Majik.Core.Stack.Stack?, Majik.Core.Game.SpellDefinition?>? _spellDefResolver;

    public TurnDriver(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Player, IPlayerAgent> agents,
        Majik.Core.Stack.Stack stack,
        ZoneService zoneService,
        TriggerManager triggerManager,
        StackResolver stackResolver,
        StateBasedActions stateBasedActions,
        PriorityManager priorityManager,
        CombatFlow combatFlow,
        Majik.Core.Effects.ContinuousEffectsService? continuousEffects = null,
        LandDropTracker? landDropTracker = null,
        Majik.Core.Events.IEventBus? eventBus = null,
        Func<ICard, Player, Majik.Core.Stack.Stack?, Majik.Core.Game.SpellDefinition?>? spellDefinitionResolver = null,
        Majik.Core.Effects.ReplacementBus? replacements = null)
    {
        _continuousEffects = continuousEffects;
        _replacements = replacements;
        // CR 305.2 — PriorityLoop requires a non-null LandDropTracker. Callers
        // that don't supply one get a fresh per-driver instance; the rule is
        // enforced uniformly regardless.
        _landDropTracker = landDropTracker ?? new LandDropTracker();
        _eventBus = eventBus;
        _spellDefResolver = spellDefinitionResolver;
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
        _stackResolver = stackResolver ?? throw new ArgumentNullException(nameof(stackResolver));
        _sba = stateBasedActions ?? throw new ArgumentNullException(nameof(stateBasedActions));
        _priorityManager = priorityManager ?? throw new ArgumentNullException(nameof(priorityManager));
        _combatFlow = combatFlow ?? throw new ArgumentNullException(nameof(combatFlow));

        // Subscribe to zone-move and draw events to keep TurnState current.
        _eventBus?.Subscribe<CardMovedEvent>(OnCardMoved);
        _eventBus?.Subscribe<CardDrawnEvent>(OnCardDrawn);
        _eventBus?.Subscribe<Majik.Core.Domain.DomainEvents.SpellCastEvent>(OnSpellCast);
    }

    // -----------------------------------------------------------------
    // TurnState event handlers
    // -----------------------------------------------------------------

    private void OnCardMoved(CardMovedEvent e)
    {
        // Track lands entering under a player's control this turn (CR 702.142
        // landfall + landfall-conditional spells like Searing Blaze). This
        // fires off the same CardMovedEvent funnel as the leavers below; the
        // entering branch must run BEFORE the early-return for non-leavers.
        if (e.ToZone == ZoneType.Battlefield && e.Card.HasType(CardType.Land))
        {
            TurnState.RecordLandEnteredBattlefield(e.Card.Controller);
        }

        // Only track permanents leaving the battlefield (Rule 702.104).
        if (e.FromZone != ZoneType.Battlefield) return;

        var formerController = e.Card.Controller;

        TurnState.RecordPermanentLeftBattlefield(formerController);

        // A creature dying = it had the Creature type while on the battlefield
        // and the move destination is anywhere it ceases to be a permanent
        // (typically Graveyard, Exile, hand, library — all qualify as "died"
        // from a tracking standpoint; Rule 700.4 defines "dies" as battlefield → graveyard,
        // but revolt and connive count any permanent leaving, so we record
        // both. The creature-death counter is additionally incremented here
        // only for cards that have the Creature type at the time they leave).
        if (e.Card.HasType(CardType.Creature))
        {
            TurnState.RecordCreatureDied(formerController);
        }
    }

    private void OnCardDrawn(CardDrawnEvent e)
    {
        TurnState.RecordCardDrawn(e.Player);
    }

    private void OnSpellCast(Majik.Core.Domain.DomainEvents.SpellCastEvent e)
    {
        // CR 105 — record the colours of every spell cast this turn so
        // "opponent has cast a [colour] spell this turn" predicates (Veil
        // of Summer) can read them at resolution.
        if (e.Spell?.Controller is { } caster && e.Spell.Card is { } card)
        {
            TurnState.RecordSpellCast(caster, Majik.Core.Cards.CardColors.GetColors(card));
        }
    }

    public async Task RunTurnAsync(Player activePlayer, int turnNumber, CancellationToken ct = default)
    {
        if (activePlayer == null) throw new ArgumentNullException(nameof(activePlayer));

        _currentTurnNumber = turnNumber;
        _activePlayerForStepEvents = activePlayer;
        _eventBus?.Publish(new Majik.Core.Events.TurnStartedEvent(activePlayer, turnNumber));

        // CR 305.2 — land drops reset at turn start.
        _landDropTracker.ResetTurn();

        // CR 119.3 — per-player life-loss counters reset at turn start.
        // Consulted by Spectacle alt-cost, Revolt, "if you lost life this
        // turn" triggers, etc. Reset before TurnState.Reset to keep
        // turn-start zeroing of all per-turn trackers in one block.
        foreach (var p in _players)
        {
            p.ResetTurnTrackers();
        }

        // Reset per-turn event tally (revolt, connive X, draw watchers).
        TurnState.Reset();

        // Beginning phase
        SetPhase(PhaseStateType.Untap);
        UntapStep(activePlayer);

        SetPhase(PhaseStateType.Upkeep);
        await PriorityRound(activePlayer, ct);

        SetPhase(PhaseStateType.Draw);
        // CR 117.5 / 614.12 — "Skip your draw step" replacement effects
        // (Necropotence, Yawgmoth's Bargain, etc.) are consulted via
        // SkipDrawRegistry. Turn 1 already skips by convention; on any
        // later turn we honour an active skip-draw predicate.
        if (turnNumber > 1 && !SkipDrawRegistry.ShouldSkipDraw(activePlayer))
        {
            DrawCard(activePlayer);
        }
        await PriorityRound(activePlayer, ct);

        // Main 1
        SetPhase(PhaseStateType.Main);
        // CR 714.2 — Saga lore-counter tick fires at the precombat main.
        AdvanceSagas(activePlayer);
        await PriorityRound(activePlayer, ct);

        // Combat
        SetPhase(PhaseStateType.BeginningOfCombat);
        await PriorityRound(activePlayer, ct);

        var defender = _players.First(p => !ReferenceEquals(p, activePlayer));
        SetPhase(PhaseStateType.DeclareAttackers);
        await RunCombat(activePlayer, defender, ct);

        // CR 506.4 — additional combat phases drain the queue.
        while (_additionalCombats.TryConsume())
        {
            SetPhase(PhaseStateType.BeginningOfCombat);
            await PriorityRound(activePlayer, ct);
            SetPhase(PhaseStateType.DeclareAttackers);
            await RunCombat(activePlayer, defender, ct);
        }
        // Per-turn reset so the queue doesn't bleed into the next turn.
        _additionalCombats.Reset();

        // Main 2
        SetPhase(PhaseStateType.Main);
        await PriorityRound(activePlayer, ct);

        // End phase
        SetPhase(PhaseStateType.End);
        await PriorityRound(activePlayer, ct);

        SetPhase(PhaseStateType.Cleanup);
        Cleanup(activePlayer);
    }

    private Player? _activePlayerForStepEvents;

    private void SetPhase(PhaseStateType phase)
    {
        _currentPhase = phase;
        // CR 500 — emit StepStartedEvent so binders for "at the beginning
        // of your upkeep / end step / draw step" triggers can fire.
        if (_activePlayerForStepEvents != null)
        {
            _eventBus?.Publish(new Majik.Core.Events.StepStartedEvent(phase, _activePlayerForStepEvents));
        }
    }

    private void UntapStep(Player active)
    {
        foreach (var card in active.Zones.Battlefield.GetCards().OfType<Permanent>().ToList())
        {
            if (card.IsTapped) card.Untap();
            // CR 502 — clears summoning sickness, loyalty-once-per-turn,
            // and any other turn-scoped per-permanent flags.
            card.ResetTurnState();
        }
    }

    private void AdvanceSagas(Player active)
    {
        // CR 714.2 — at the precombat main, each Saga its controller
        // controls adds a lore counter and triggers the matching chapter
        // ability. SagaState.AdvanceAndChapter invokes the onChapter
        // callback synchronously; the chapter's effect (token spawn,
        // etc.) lands immediately. Future cut: route through the stack
        // so the chapter ability respects priority + responses.
        foreach (var perm in active.Zones.Battlefield.GetCards()
                     .OfType<Permanent>().ToList())
        {
            perm.SagaState?.AdvanceAndChapter();
        }
    }

    private void DrawCard(Player player)
    {
        var top = player.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            // CR 704.5b — draw from empty library flags the player for
            // state-based loss. Without this flag, the game can stall
            // forever (no win condition fires).
            player.TriedToDrawFromEmptyLibrary = true;
            return;
        }
        player.Zones.Library.RemoveCard(top);
        player.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }

    private async Task PriorityRound(Player activePlayer, CancellationToken ct)
    {
        // Use the canonical bus if injected so SpellCastEvent reaches the
        // same subscribers as zone/stack/SBA events. Fallback: local bus
        // (events not externally visible — preserves prior behaviour).
        var castBus = _eventBus ?? new Majik.Core.Events.EventBus();
        var castFlow = new SpellCastFlow(_stack, _zoneService, castBus);
        // Pass the layer service so CR 305.6 retyping (Blood Moon, etc.)
        // reshapes mana sources at payment time. Null when the driver
        // was constructed without a continuous-effects service — the
        // resolver falls back to printed mana abilities.
        var manaResolver = new Majik.Core.Costs.ManaPaymentResolver(_continuousEffects);

        async Task DispatchCast(Player actor, PriorityAction.CastSpell cast, GameContext ctx)
        {
            static void RotateHand(ICard card, string reason)
            {
                // Bot's per-turn failed-cards memo handles the "don't re-
                // propose" side; this rotation is now a vestigial nudge.
                // Kept because some agents may not memo failures, and the
                // rotation also helps the bot iterate through alternatives
                // by changing hand order between sweeps.
                if (card.Owner != null && card.Zone == Majik.Core.Zones.ZoneType.Hand)
                {
                    card.Owner.Zones.Hand.RemoveCard(card);
                    card.Owner.Zones.Hand.AddCard(card);
                }
            }

            // Resolve a proper SpellDefinition via the injected resolver
            // (oracle-text → effects binder). Fall back to vanilla — fine
            // for permanents (StackResolver puts them on the battlefield);
            // for instants/sorceries with no binder match, casting would
            // waste the card, so we skip and rotate.
            var resolved = _spellDefResolver?.Invoke(cast.Card, actor, _stack);
            var isPermanent = cast.Card.HasType(Majik.Core.Cards.Types.CardType.Creature)
                || cast.Card.HasType(Majik.Core.Cards.Types.CardType.Artifact)
                || cast.Card.HasType(Majik.Core.Cards.Types.CardType.Enchantment)
                || cast.Card.HasType(Majik.Core.Cards.Types.CardType.Planeswalker);
            if (resolved == null && !isPermanent)
            {
                RotateHand(cast.Card, "no SpellDef for instant/sorcery");
                return;
            }
            var def = resolved
                ?? Majik.Core.Game.SpellDefinition.Vanilla(_ => Array.Empty<Majik.Core.Abilities.IEffect>());

            // Pay mana up front. SpellCastFlow doesn't enforce payment;
            // it just collects ManaPayment for downstream metadata.
            // When the agent elected an alternative cost (CR 118.9 —
            // flashback / spectacle / evoke / pitch), it REPLACES the
            // printed cost and bypasses cost-reduction; otherwise apply
            // CR 117.7 Affinity / cost-reducers on the printed cost.
            var cost = cast.AlternativeCost?.AlternativeManaCost
                ?? Majik.Core.Costs.CostReduction.GetEffectiveCost(cast.Card, actor);

            // CR 601.2g + CR 106.4 — pay from the player's already-floating
            // mana pool first. When the pool fully covers the cost we don't
            // need to prompt the agent for sources at all (drag-to-cast UX
            // in the portal: float mana via ActivateManaAbilityCommand,
            // then cast and have the cost paid silently). Hybrid/Phyrexian
            // pips need agent input even when raw colour counts add up, so
            // we restrict the auto-pay short-circuit to plain WUBRG+generic
            // costs. ManaPaymentResolver.Pay with an empty source list
            // still consumes from the actual pool — same code path the
            // existing prompt route hits when the agent picks no sources.
            var canAutoPayFromPool = cost.HybridPips.Count == 0
                && cost.PhyrexianPips.Count == 0
                && actor.ManaPool.CanPay(cost);

            ManaPayment payment;
            if (canAutoPayFromPool)
            {
                payment = Majik.Core.Players.Agents.ManaPayment.Empty;
            }
            else
            {
                payment = await _agents[actor].ChooseManaSourcesAsync(ctx, cost, ct);
                // CR 601.2 / CR 727 — remote player aborted the cast at
                // the cost-payment prompt. Nothing has been paid yet
                // (the resolver hasn't run), so the spell simply stays
                // in hand. No SpellCastEvent, no priority change.
                if (payment.IsCancelled)
                {
                    return;
                }
            }
            if (!manaResolver.Pay(actor, cost, payment))
            {
                RotateHand(cast.Card, "Pay failed");
                return;
            }

            try
            {
                // Forward the already-prompted mana payment so SpellCastFlow
                // doesn't re-prompt (CR 601.2g — one mana selection per cast).
                await castFlow.CastAsync(
                    actor, cast.Card, def, _agents[actor], ctx, ct,
                    additionalCosts: cast.AdditionalCosts,
                    alternativeCost: cast.AlternativeCost,
                    preChosenMana: payment);
            }
            catch (InvalidOperationException ex)
            {
                RotateHand(cast.Card, $"CastAsync threw: {ex.Message}");
            }
        }

        async Task DispatchActivate(Player actor, PriorityAction.ActivateAbility activate, GameContext ctx)
        {
            // CR 602.2 — activate an ability via AbilityActivator. For each
            // TargetRequest on the ability, ask the agent to choose targets
            // (the bot's ChooseTargetsAsync ranks intelligently); wrap each
            // chosen object as an ITarget so AbilityActivator can consume
            // it. v1 picks the first chosen per request — multi-target
            // requests beyond MinTargets=1 are supported but currently
            // collapsed to one wrapper per chosen object.
            var targets = new List<Majik.Core.Targeting.ITarget>();
            if (activate.Ability is Majik.Core.Abilities.ActivatedAbility aa)
            {
                foreach (var req in aa.TargetRequests)
                {
                    var chosen = await _agents[actor].ChooseTargetsAsync(ctx, req, ct: default);
                    foreach (var obj in chosen)
                    {
                        var wrapper = obj switch
                        {
                            Majik.Core.Cards.Permanent perm => Majik.Core.Targeting.Target.Permanent(perm),
                            Majik.Core.Cards.ICard card => Majik.Core.Targeting.Target.Card(card),
                            Player p => Majik.Core.Targeting.Target.Player(p),
                            Majik.Core.Spells.ISpell spell => Majik.Core.Targeting.Target.Spell(spell),
                            Majik.Core.Abilities.IActivatedAbility ab => Majik.Core.Targeting.Target.Ability(ab),
                            _ => null,
                        };
                        if (wrapper != null) targets.Add(wrapper);
                    }
                }
            }

            var activator = new Majik.Core.Services.AbilityActivator(_stack, _eventBus);
            try
            {
                activator.ActivateAbility(activate.Ability, actor, targets, activate.Ability.Costs);
            }
            catch (InvalidOperationException)
            {
                // Cost-payment or zone-gate failed — swallow and let the
                // priority pump move on. Bot's per-turn memo prevents
                // re-proposing this same ability.
            }
        }

        var manaActivator = new Majik.Core.Services.ManaAbilityActivator(_eventBus);
        void DispatchManaAbility(Player actor, PriorityAction.ActivateManaAbility ma)
        {
            try
            {
                manaActivator.ActivateManaAbility(ma.Ability, actor);
            }
            catch (InvalidOperationException)
            {
                // Mirror DispatchActivate's posture: swallow validation
                // failures (wrong controller / CanActivate false) so the
                // pump keeps moving instead of tearing down the round.
            }
            catch (Majik.Core.Domain.Exceptions.InvalidPlayerActionException)
            {
                // ManaAbilityActivator's own validation throw — same
                // posture as above.
            }
        }

        var loop = new PriorityLoop(
            players: _players,
            priority: _priorityManager,
            stack: _stack,
            stackResolver: _stackResolver,
            zoneService: _zoneService,
            agents: _agents,
            turnNumberAccessor: () => _currentTurnNumber,
            phaseAccessor: () => _currentPhase,
            // CR 305.2 — every priority round in this turn must consult the
            // same LandDropTracker the driver reset at turn-start; otherwise
            // the per-turn one-land cap is unenforced and a bot proposing
            // PlayLand twice in one main phase succeeds twice. The tracker
            // is optional (null in test harnesses that construct TurnDriver
            // without one), in which case PriorityLoop falls back to its
            // old no-op behaviour.
            landDropTracker: _landDropTracker,
            castDispatcher: DispatchCast,
            activateDispatcher: DispatchActivate,
            manaAbilityDispatcher: DispatchManaAbility);

        await loop.RunUntilRoundEndsAsync(activePlayer, ct);
    }

    private async Task RunCombat(Player attacker, Player defender, CancellationToken ct)
    {
        var eligibleAttackers = attacker.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !c.HasSummoningSickness && !c.IsTapped)
            .ToList();
        var eligibleBlockers = defender.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !c.IsTapped)
            .ToList();

        var ctx = new GameContext(
            attacker, _players, attacker, _currentTurnNumber, _currentPhase, _stack);

        await _combatFlow.RunCombatAsync(
            attacker, defender,
            _agents[attacker], _agents[defender],
            eligibleAttackers, eligibleBlockers, ctx, ct);

        // Priority round after damage (Rule 510.2 — players get priority).
        await PriorityRound(attacker, ct);
    }

    private void Cleanup(Player active)
    {
        // 1. Discard down to hand size (default 7).
        const int maxHandSize = 7;
        var hand = active.Zones.Hand.GetCards().ToList();
        while (hand.Count > maxHandSize)
        {
            var discard = hand[0]; // simplification: first card
            active.Zones.Hand.RemoveCard(discard);
            active.Zones.Graveyard.AddCard(discard);
            discard.SetZone(ZoneType.Graveyard);
            hand.RemoveAt(0);
        }

        // 2. Remove damage from creatures.
        //    Also drop any remaining regeneration shields (CR 701.15a /
        //    CR 514.2 — shields are "until end of turn"). Done in the
        //    same battlefield sweep so the EOT pass touches each permanent
        //    once.
        foreach (var permanent in _players.SelectMany(p => p.Zones.Battlefield.GetCards().OfType<Permanent>()))
        {
            if (permanent is Creature creature) creature.ClearDamage();
            permanent.ClearRegenerationShields();
        }

        // 3. Empty mana pools.
        foreach (var p in _players)
        {
            p.EmptyManaPool();
        }

        // 4. "Until end of turn" continuous effects expire (CR 514.2).
        _continuousEffects?.ExpireEndOfTurn();

        // 5. Per-turn replacement shields (Fog, "prevent next N damage")
        // expire alongside the continuous-effect layer.
        _replacements?.ExpireEndOfTurn();
    }
}
