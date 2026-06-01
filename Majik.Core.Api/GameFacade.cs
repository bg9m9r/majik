using System.Text.Json;
using Majik.Core.Abilities;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Api;

/// <summary>
/// Per-game aggregate exposed to the outside world. Wraps a live engine
/// (event bus, stack, players, trigger manager, priority loop) with a
/// command/state/events interface that's safe to ship over HTTP/JSON.
///
/// Two run modes:
/// - <see cref="StartAsync"/> runs a single priority round on Alice
///   (legacy Phase-9 behavior used by most existing tests).
/// - <see cref="StartFullGameAsync"/> runs the full
///   <see cref="GameDriver"/> loop: shuffle, mulligan, multi-turn,
///   state-based actions, combat, win-condition checks (Phase 10).
/// </summary>
public sealed class GameFacade : IDisposable
{
    /// <summary>
    /// Process-wide sink invoked when a per-match <see cref="EventBus"/>
    /// handler throws. Without this wired, <see cref="EventBus.OnHandlerError"/>
    /// stays null and every handler exception vanishes silently — a thrown
    /// trigger / SBA / wire-bridge handler would never surface, freezing a
    /// match with no diagnostic. The server composition root assigns this
    /// to a structured logger at startup (see
    /// <c>MatchRegistration.AddMajikMatches</c>); tests can swap it to
    /// capture failures. Majik.Core intentionally takes no dependency on
    /// Microsoft.Extensions.Logging, so this is a plain delegate seam.
    ///
    /// <para>In DEBUG builds the default (when left null) FAILS FAST: a
    /// handler exception is rethrown on a background thread so it surfaces
    /// loudly in tests / dev runs instead of being swallowed. In RELEASE
    /// the default is a no-op (the bus still continues delivery to other
    /// handlers) — the server overrides it with a logger so production
    /// failures are observed, never silent.</para>
    /// </summary>
    public static Action<Exception, GameEvent>? OnEventHandlerError { get; set; }

    /// <summary>
    /// Feature flag (DEFAULT TRUE): when on, factory-backed NON-LAND deck
    /// cards are built through their <c>[CardName]</c> <c>*Factory</c> during
    /// <see cref="Create"/> rather than arriving as ability-less binder-chain
    /// shells. The per-card factories were historically TEST-ONLY (only
    /// <c>NamedCardFactory.Create</c> dispatched to them, which prod never
    /// called), so bespoke factory abilities — Agatha's Soul Cauldron's
    /// <c>{T}</c>, Dredger's Insight's JSON ETB triggers, … — did nothing in a
    /// real match even though their unit tests passed. Routing closes that
    /// gap. Flip to <c>false</c> to fall back to the pure binder chain without
    /// a revert (kill-switch). Lands are NEVER routed regardless — their
    /// factories deliberately omit enters-tapped/shock and rely on the binder
    /// chain (see <c>SurveilLandCycleFactory</c> / <c>ShockLandBinder</c>).
    /// </summary>
    public static bool RouteThroughNamedFactories
    {
        get => Majik.Core.CardData.FactoryRouting.RouteThroughNamedFactories;
        set => Majik.Core.CardData.FactoryRouting.RouteThroughNamedFactories = value;
    }

    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly PriorityManager _priority;
    private readonly StateBasedActions _sba;
    private readonly CombatFlow _combatFlow;
    private readonly Player _alice;
    private readonly Player _bob;
    private ICardRepository? _cardRepo;
    private ScryfallCardFactory? _spellFactory;
    private readonly RemoteAgent _aliceAgent;
    private readonly RemoteAgent _bobAgent;
    private IPlayerAgent _aliceAgentEffective;
    private IPlayerAgent _bobAgentEffective;
    private PriorityLoop _loop;
    private Task? _loopTask;
    private Task<GameDriver.GameResult>? _fullGameTask;
    private readonly List<Action<EventDto>> _subscribers = new();
    private readonly List<Action<EventEnvelope>> _envelopeSubscribers = new();
    private readonly ActionLog _log = new();

    // PLAN 04 — per-game monotonic sequence counter. Every event stamped in
    // BridgeEvent draws the next value; a state snapshot reports _lastEventSeq
    // (the seq of the last event folded in) WITHOUT advancing the counter, so
    // snapshot.Seq == the most recent event's Seq. The portal drops any
    // snapshot older than its current state and detects dropped events by
    // contiguity (next event seq must equal current+1). One game runs on one
    // logical thread; Interlocked guards against any incidental concurrency.
    private long _seq;
    private long _lastEventSeq;

    private long NextSeq() => Interlocked.Increment(ref _seq);

    // Synchronization signal that pulses every time an agent registers a
    // new prompt. StartAsync / SubmitAsync capture this BEFORE poking the
    // engine, then await it — so they return only once the engine has
    // settled at its next prompt (or the round/game has finished). This
    // replaces the previous `Task.Delay(1)` magic sleep, which was racy
    // on slow CI runners and produced flaky "Agent has no pending prompt"
    // failures on subsequent submits.
    private TaskCompletionSource<bool> _nextPromptSignal = NewSignal();

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void PulsePromptSignal()
    {
        var prev = Interlocked.Exchange(ref _nextPromptSignal, NewSignal());
        prev.TrySetResult(true);
    }
    private int _currentTurn = 1;
    private PhaseStateType _currentPhase = PhaseStateType.PreCombatMain;
    private Player? _currentActivePlayer;
    // Since Slice 3 the phase value already carries the precombat /
    // postcombat distinction (CR 505), so the wire/UI labels need no
    // disambiguation. The outer TurnStateMachine state is still tracked for
    // TurnStateChangedEvent payloads and other turn-state consumers.
    private TurnStateType? _currentTurnState;

    public Guid GameId { get; } = Guid.NewGuid();

    public bool IsRoundComplete => _loopTask?.IsCompleted ?? false;

    /// <summary>The Alice/creator-slot player. Exposed so tooling can wire
    /// a non-RemoteAgent (e.g. <see cref="Majik.Bot.BotPlayerAgent"/>) before
    /// the game starts.</summary>
    public Player Alice => _alice;

    /// <summary>The Bob/opponent-slot player. See <see cref="Alice"/>.</summary>
    public Player Bob => _bob;

    /// <summary>
    /// The id of the engine's current active player (CR 102.1 / 103.7 —
    /// "the active player is the player whose turn it is"). Tracked from
    /// <see cref="TurnStartedEvent"/>; falls back to the priority holder
    /// and finally to <see cref="Alice"/> before any turn has started so
    /// the server clock and wire contract always have a non-empty id to
    /// DERIVE the clock holder / seat identity from rather than recompute.
    /// </summary>
    public Guid ActivePlayerId => (_currentActivePlayer ?? _priority.CurrentPlayer ?? _alice).Id;

    /// <summary>
    /// The game's replacement-effect bus. Binders (e.g. ShockLandBinder)
    /// register handlers here during deck load; ZoneService reads from it on
    /// every card move so replacements fire correctly at the table.
    /// </summary>
    public ReplacementBus Replacements { get; } = new();

    /// <summary>
    /// CR 613 layer-system executor for this game. Binders that produce
    /// keyword-style continuous effects (Prowess pump, until-end-of-turn
    /// buffs/debuffs, combat restrictions) register onto this instance;
    /// creatures consult it via <see cref="Majik.Core.Cards.Creature.ActiveEffects"/>
    /// to compute current characteristics. TurnDriver expires EOT effects
    /// during the cleanup step.
    /// </summary>
    public ContinuousEffectsService ContinuousEffects { get; } = new();

    /// <summary>
    /// CR 305.2 — per-turn land-drop counter. Shared between the legacy
    /// single-round <see cref="StartAsync"/> path and the
    /// <see cref="StartFullGameAsync"/> driver loop so both enforce the
    /// one-land-per-turn cap against the same instance. Without this
    /// sharing, the priority loop would have no tracker and bots could
    /// play multiple lands per turn.
    /// </summary>
    public LandDropTracker LandDrops { get; } = new();

    private GameFacade(Player alice, Player bob)
    {
        _alice = alice;
        _bob = bob;

        // Route per-match EventBus handler exceptions to the process-wide
        // sink so a throwing trigger / SBA / wire-bridge handler can't vanish
        // and freeze the match with no diagnostic. Fail loud: log (server) or
        // fail-fast (DEBUG default) instead of swallowing silently.
        _bus.OnHandlerError = HandleBusHandlerError;

        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _zones = new ZoneService(_bus, Replacements);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _resolver = new StackResolver(_bus, _zones, _sba);
        _priority = new PriorityManager(new List<Player> { alice, bob }, _stack, _bus, _triggers);
        _combatFlow = new CombatFlow(_bus, _sba, Replacements);

        _aliceAgent = new RemoteAgent(alice, LookupCard, LookupPlayer);
        _bobAgent = new RemoteAgent(bob, LookupCard, LookupPlayer);
        _aliceAgent.PromptRequested += _ => PulsePromptSignal();
        _bobAgent.PromptRequested += _ => PulsePromptSignal();

        _aliceAgentEffective = _aliceAgent;
        _bobAgentEffective = _bobAgent;

        // Register both seats' agents so effect closures that can't receive
        // an agent parameter (the v1 sync effect model — fetchland tutors,
        // Sakura-Tribe Elder sacrifice, Path to Exile, Wish, Mox Diamond,
        // etc.) can prompt the right player at resolution. Pre-fix this was
        // never called and AgentRegistry.Get(player) always returned null,
        // so every closure silently fell back to candidates[0] — the user
        // never saw a prompt at the live table.
        AgentRegistry.Set(_alice, _aliceAgentEffective);
        AgentRegistry.Set(_bob, _bobAgentEffective);

        _loop = BuildPriorityLoop();

        _bus.SubscribeAll(BridgeEvent);
        _bus.Subscribe<TurnStartedEvent>(e => { _currentTurn = e.TurnNumber; _currentActivePlayer = e.Player; });
        _bus.Subscribe<PhaseStartedEvent>(e => { _currentPhase = e.PhaseType; });
        _bus.Subscribe<StepStartedEvent>(e => { _currentPhase = e.StepType; });
        _bus.Subscribe<TurnStateChangedEvent>(e => { _currentTurnState = e.CurrentState; });
    }

    private PriorityLoop BuildPriorityLoop()
    {
        var agents = new Dictionary<Player, IPlayerAgent>
        {
            [_alice] = _aliceAgentEffective,
            [_bob] = _bobAgentEffective,
        };

        // Mirror TurnDriver.PriorityRound's dispatcher wiring (Majik.Core/Game/TurnDriver.cs)
        // so the legacy single-round StartAsync path can also accept
        // CastSpellCommand / activated-ability commands without throwing
        // "PriorityLoop received CastSpell but no castDispatcher was supplied."
        //
        // GameFacade has no spellDefResolver, so instants/sorceries without
        // a binder match are skipped (the card is rotated in hand). Permanents
        // and abilities still resolve through SpellCastFlow + AbilityActivator.
        var castFlow = new SpellCastFlow(_stack, _zones, _bus);
        var manaResolver = new Majik.Core.Costs.ManaPaymentResolver(ContinuousEffects);

        async Task DispatchCast(Player actor, PriorityAction.CastSpell cast, GameContext ctx)
        {
            var isPermanent = cast.Card.HasType(Majik.Core.Cards.Types.CardType.Creature)
                || cast.Card.HasType(Majik.Core.Cards.Types.CardType.Artifact)
                || cast.Card.HasType(Majik.Core.Cards.Types.CardType.Enchantment)
                || cast.Card.HasType(Majik.Core.Cards.Types.CardType.Planeswalker);
            if (!isPermanent)
            {
                // No spell-def resolver in this facade — skip non-permanent
                // casts rather than waste the card.
                return;
            }

            var agent = agents[actor];
            // CR 117.7 / 601.2f — pass the live player roster so the
            // three-arg overload folds in SpellCostIncreaseAbility riders
            // (Sphere of Resistance, Trinisphere, Thalia, Damping Sphere)
            // at cast-time, matching TurnDriver.PriorityRound.
            var cost = cast.AlternativeCost?.AlternativeManaCost
                ?? Majik.Core.Costs.CostReduction.GetEffectiveCost(cast.Card, actor, ctx.AllPlayers);

            // CR 601.2g + CR 106.4 — pay from floating pool first when it
            // fully covers the cost (drag-to-cast UX: float via
            // ActivateManaAbilityCommand, then cast silently). Mirrors
            // TurnDriver.DispatchCast — see comment there for rationale
            // and the hybrid/Phyrexian guardrail.
            var canAutoPayFromPool = cost.HybridPips.Count == 0
                && cost.PhyrexianPips.Count == 0
                && actor.ManaPool.CanPay(cost);

            ManaPayment payment;
            if (canAutoPayFromPool)
            {
                payment = ManaPayment.Empty;
            }
            else
            {
                payment = await agent.ChooseManaSourcesAsync(ctx, cost, CancellationToken.None);
                // CR 601.2 / CR 727 — CancelCastCommand surfaces as the
                // Cancelled sentinel. Spell stays in hand, nothing fires.
                if (payment.IsCancelled)
                {
                    return;
                }

                // Portal "Auto-pay" — empty (non-cancelled) source list means
                // "tap my untapped lands for me". When the floating pool can't
                // already cover the cost, auto-select untapped sources.
                // Mirrors TurnDriver.DispatchCast.
                if (payment.Sources.Count == 0
                    && !actor.ManaPool.CanPay(cost)
                    && manaResolver.TryAutoSelectSources(actor, cost, out var autoPayment))
                {
                    payment = autoPayment;
                }
            }
            if (!manaResolver.Pay(actor, cost, payment))
            {
                return;
            }

            try
            {
                var def = Majik.Core.Game.SpellDefinition.Vanilla(_ => Array.Empty<IEffect>());
                await castFlow.CastAsync(
                    actor, cast.Card, def, agent, ctx, CancellationToken.None,
                    additionalCosts: cast.AdditionalCosts,
                    alternativeCost: cast.AlternativeCost,
                    preChosenMana: payment);
            }
            catch (InvalidOperationException)
            {
                // Swallow — same posture as TurnDriver. Failure leaves the
                // card in hand; bot/agent memo prevents re-proposing.
            }
        }

        async Task DispatchActivate(Player actor, PriorityAction.ActivateAbility activate, GameContext ctx)
        {
            var targets = new List<Majik.Core.Targeting.ITarget>();
            if (activate.Ability is Majik.Core.Abilities.ActivatedAbility aa)
            {
                foreach (var req in aa.TargetRequests)
                {
                    var chosen = await agents[actor].ChooseTargetsAsync(ctx, req, ct: default);
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

            var activator = new Majik.Core.Services.AbilityActivator(_stack, _bus);
            try
            {
                activator.ActivateAbility(activate.Ability, actor, targets, activate.Ability.Costs);
            }
            catch (InvalidOperationException)
            {
                // Cost-payment or zone-gate failed — let the priority loop move on.
            }
        }

        var manaActivator = new ManaAbilityActivator(_bus);
        void DispatchManaAbility(Player actor, PriorityAction.ActivateManaAbility ma)
        {
            try
            {
                manaActivator.ActivateManaAbility(ma.Ability, actor);
            }
            catch (InvalidOperationException)
            {
                // CanActivate / wrong-controller failure — swallow so the
                // priority pump keeps moving. Same posture as DispatchCast /
                // DispatchActivate above.
            }
            catch (Majik.Core.Domain.Exceptions.InvalidPlayerActionException)
            {
                // Mirrors ManaAbilityActivator's own validation throw
                // (wrong controller / CanActivate false).
            }
        }

        return new PriorityLoop(
            players: new[] { _alice, _bob },
            priority: _priority,
            stack: _stack,
            stackResolver: _resolver,
            zoneService: _zones,
            agents: agents,
            turnNumberAccessor: () => 1,
            phaseAccessor: () => PhaseStateType.PreCombatMain,
            // CR 305.2 — share the facade's per-turn land-drop counter so
            // PlayLandCommand submissions are gated on the same instance
            // a subsequent StartFullGameAsync run also uses.
            landDropTracker: LandDrops,
            castDispatcher: DispatchCast,
            activateDispatcher: DispatchActivate,
            manaAbilityDispatcher: DispatchManaAbility);
    }

    /// <summary>
    /// Build the spell-definition resolver TurnDriver consults at cast time
    /// for non-permanent spells (Lava Spike, Lightning Bolt, Boltwave, etc.).
    /// Without it, TurnDriver.DispatchCast hits the "no SpellDef for
    /// instant/sorcery" branch and rotates the card back into hand — the
    /// card is never actually cast. The resolver mirrors what
    /// TriggerPlayground wires for its bot vs bot face-off (and what
    /// MatchService implicitly relied on via the card repo) so every
    /// instant/sorcery whose oracle text matches an OracleSpellBinder
    /// template gets a runnable SpellDefinition. Returns null when no card
    /// repo was supplied at <see cref="Create"/> time — tests that build
    /// purely synthetic decks (no repo) keep their existing skip-rotate
    /// behaviour.
    /// </summary>
    private Func<ICard, Player, Majik.Core.Stack.Stack?, SpellDefinition?>? BuildSpellDefinitionResolver()
    {
        if (_cardRepo == null) return null;
        _spellFactory ??= new ScryfallCardFactory(
            _cardRepo,
            replacements: Replacements,
            effects: ContinuousEffects,
            triggers: _triggers,
            eventBus: _bus,
            zones: _zones);

        return (card, caster, stk) =>
            _spellFactory.LookupSpellDefinition(card.Name, caster, raw => raw, stk);
    }

    /// <summary>Replaces the Alice-seat agent (typically with a
    /// <see cref="Majik.Bot.BotPlayerAgent"/>). Must be called before
    /// <see cref="StartAsync"/> / <see cref="StartFullGameAsync"/>; throws
    /// once the game is running. After swap, <see cref="SubmitAsync"/> for
    /// that seat will throw — the bot drives itself.</summary>
    public void ReplaceAliceAgent(IPlayerAgent agent) => SwapAgent(_alice, agent);

    /// <summary>Replaces the Bob-seat agent. See <see cref="ReplaceAliceAgent"/>.</summary>
    public void ReplaceBobAgent(IPlayerAgent agent) => SwapAgent(_bob, agent);

    private void SwapAgent(Player seat, IPlayerAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (_loopTask != null || _fullGameTask != null)
            throw new InvalidOperationException("Cannot replace agent after the game has started.");

        if (ReferenceEquals(seat, _alice)) _aliceAgentEffective = agent;
        else _bobAgentEffective = agent;

        // Mirror the swap into AgentRegistry so effect closures (fetchland
        // tutor, Sakura-Tribe Elder, Path to Exile, Wish, Mox Diamond, etc.)
        // prompt the seat's NEW agent — otherwise a bot swap would still
        // hit the RemoteAgent originally registered at construction time.
        AgentRegistry.Set(seat, agent);

        _loop = BuildPriorityLoop();
    }

    public static GameFacade Create(
        string aliceName,
        string bobName,
        IReadOnlyList<ICard> aliceDeck,
        IReadOnlyList<ICard> bobDeck,
        ICardRepository? cardRepo = null,
        ReplacementBus? replacements = null)
    {
        var alice = new Player(aliceName, 20);
        var bob = new Player(bobName, 20);

        // Construct the facade first so its internal ReplacementBus is available.
        // Any externally-supplied bus is ignored — binders register onto the
        // facade's own bus, which ZoneService already holds a reference to.
        var facade = new GameFacade(alice, bob);
        var bus = facade.Replacements;

        // Stash the repo so StartFullGameAsync can build a spell-definition
        // resolver later. Without this, TurnDriver.DispatchCast falls through
        // to "no SpellDef for instant/sorcery" and silently rotates the card
        // back into hand — every non-permanent spell becomes uncastable.
        facade._cardRepo = cardRepo;

        foreach (var card in aliceDeck)
        {
            var live = BuildDeckCard(card, alice, cardRepo, bus, facade.ContinuousEffects);
            alice.Zones.GetZone(ZoneType.Library).AddCard(live);
        }
        foreach (var card in bobDeck)
        {
            var live = BuildDeckCard(card, bob, cardRepo, bus, facade.ContinuousEffects);
            bob.Zones.GetZone(ZoneType.Library).AddCard(live);
        }

        return facade;
    }

    /// <summary>
    /// Produce the live deck-card instance for <paramref name="shell"/>.
    ///
    /// <para>Default path (binder chain): set owner on the incoming shell and
    /// run <see cref="BindCardAbilities"/> on it — historical behaviour.</para>
    ///
    /// <para>Routed path (<see cref="RouteThroughNamedFactories"/> on AND
    /// <see cref="Majik.Core.CardData.Factories.ImplementedCardNames.HasRealFactory"/>
    /// AND the card is NOT a Land): build a fresh instance via the card's
    /// <c>[CardName]</c> factory so its bespoke abilities (closures that
    /// capture the factory's own card as <c>source</c>) function in prod. The
    /// returned instance REPLACES the shell. We then re-stamp the color
    /// indicator the shell loader applied (CR 202.2c) and overlay only the
    /// additive/generic binders (keywords, mana, and the ETB
    /// replacement/counter/copy binders) with a dedup guard. We deliberately
    /// do NOT run the triggered-ability / saga / affinity / loyalty binders
    /// on routed cards — the factory owns those bespoke abilities and the
    /// binders would double-add ETB triggers.</para>
    /// </summary>
    private static ICard BuildDeckCard(
        ICard shell,
        Player owner,
        ICardRepository? cardRepo,
        ReplacementBus replacements,
        ContinuousEffectsService effects)
    {
        if (RouteThroughNamedFactories
            && !shell.HasType(Majik.Core.Cards.Types.CardType.Land)
            && Majik.Core.CardData.Factories.ImplementedCardNames.HasRealFactory(shell.Name))
        {
            // APPROACH B — instance swap. Build the card through its factory
            // (owner == controller, matching what GameFacade already assigns).
            var built = Majik.Core.CardData.NamedCardFactory.Create(shell.Name, owner);

            // Hook the CES so creatures get layer-system P/T (mirrors
            // BindCardAbilities). The factory does not wire this.
            if (built is Majik.Core.Cards.Creature creature)
            {
                creature.ActiveEffects = effects;
            }

            var entity = cardRepo?.GetByName(shell.Name);
            if (entity != null)
            {
                // Re-apply the color indicator the shell loader stamped
                // (CR 202.2c) — the factory may not. SetColorIndicator is
                // idempotent; the union with mana-cost pips is duplicate-safe.
                if (built is Majik.Core.Cards.Card concrete)
                {
                    var colors = Majik.Core.Cards.CardColors.ParseScryfallColors(entity.Colors);
                    if (colors.Count > 0) concrete.SetColorIndicator(colors);
                }

                OverlayAdditiveBinders(built, entity, owner, replacements, effects);
            }

            return built;
        }

        shell.SetOwner(owner);
        BindCardAbilities(shell, owner, cardRepo, replacements, effects);
        return shell;
    }

    /// <summary>
    /// Overlay ONLY the additive/generic binders onto a factory-built card,
    /// each guarded so a keyword or mana ability the factory already added is
    /// never doubled. Runs the ETB replacement/counter/copy chain (which the
    /// factory may not register for non-land permanents). Does NOT run the
    /// triggered-ability / saga / affinity / loyalty binders — the factory
    /// owns those.
    /// </summary>
    private static void OverlayAdditiveBinders(
        ICard card,
        CardEntity entity,
        Player controller,
        ReplacementBus replacements,
        ContinuousEffectsService effects)
    {
        // Snapshot the keyword + mana abilities the factory already attached
        // so the binders' additions can be deduped.
        var existingKeywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hadManaAbility = card.Abilities.OfType<Majik.Core.Abilities.IManaAbility>().Any();

        // KeywordBinder — dedup any keyword the factory already added.
        var beforeKeyword = card.Abilities.OfType<KeywordAbility>().Count();
        KeywordBinder.Bind(card, entity, controller, effects);
        DedupKeywordAbilities(card, existingKeywords, beforeKeyword);

        // OracleManaBinder — if the factory already gave the card a mana
        // ability, skip the binder entirely (it cannot know the factory's
        // intent and would double the source).
        if (!hadManaAbility)
        {
            OracleManaBinder.Bind(card, entity, controller);
        }

        // ETB replacement / counter / copy chain — generic, additive, and not
        // something a non-land factory typically registers. Mirrors
        // ScryfallCardFactory so the two binder entry points don't diverge.
        if (!ShockLandBinder.Bind(card, entity, replacements) &&
            !SubtypeEntersTappedBinder.Bind(card, entity, replacements) &&
            !ConditionalEntersTappedBinder.Bind(card, entity, replacements))
        {
            EntersTappedBinder.Bind(card, entity, replacements);
        }
        EntersWithCountersBinder.Bind(card, entity, replacements);
        EntersAsCopyBinder.Bind(card, entity, replacements, effects);
    }

    /// <summary>
    /// Remove any <see cref="KeywordAbility"/> the binder just added whose
    /// keyword the factory had already attached (snapshot in
    /// <paramref name="preexisting"/>). <paramref name="boundBefore"/> is the
    /// keyword-ability count before the binder ran, so we only inspect the
    /// abilities the binder appended.
    /// </summary>
    private static void DedupKeywordAbilities(
        ICard card, HashSet<string> preexisting, int boundBefore)
    {
        if (card is not Card concrete) return;
        var keywordAbilities = card.Abilities.OfType<KeywordAbility>().ToList();
        for (var i = keywordAbilities.Count - 1; i >= boundBefore; i--)
        {
            if (preexisting.Contains(keywordAbilities[i].Keyword))
            {
                concrete.RemoveAbility(keywordAbilities[i]);
            }
        }
    }

    /// <summary>
    /// Attaches abilities to a card after its owner is set. When
    /// <paramref name="cardRepo"/> is provided the full binder pipeline
    /// runs (KeywordBinder, OracleManaBinder, AffinityBinder, SagaBinder,
    /// OracleTriggeredAbilityBinder, ShockLandBinder). Without a repo only
    /// the basic-land mana path fires — preserving pre-existing behaviour.
    ///
    /// <paramref name="replacements"/> is the game's own ReplacementBus and
    /// is always non-null when called from <see cref="Create"/>; ShockLandBinder
    /// registers onto it unconditionally whenever the repo returns a matching
    /// shock-land entity (CR 614).
    /// </summary>
    private static void BindCardAbilities(
        ICard card,
        Player controller,
        ICardRepository? cardRepo,
        ReplacementBus replacements,
        ContinuousEffectsService effects)
    {
        // Every creature consults the game's CES for current P/T and keywords
        // (CR 613). Hook it up regardless of repo presence so vanilla creatures
        // still get layer-system computation when other effects target them.
        if (card is Majik.Core.Cards.Creature creature)
        {
            creature.ActiveEffects = effects;
        }

        if (cardRepo != null)
        {
            var entity = cardRepo.GetByName(card.Name);
            if (entity != null)
            {
                KeywordBinder.Bind(card, entity, controller, effects);
                OracleManaBinder.Bind(card, entity, controller);
                AffinityBinder.Bind(card, entity);
                SagaBinder.Bind(card, entity);
                foreach (var trig in OracleTriggeredAbilityBinder.Bind(card, entity, controller))
                {
                    card.AddAbility(trig);
                }
                // ETB replacement chain (CR 614). Reconciles a long-standing
                // asymmetry with ScryfallCardFactory: BindCardAbilities used to
                // register ONLY the shock-land replacement, so subtype /
                // conditional / unconditional enters-tapped lands (surveil
                // lands, "this land enters tapped", check lands) entered
                // UNTAPPED in real matches. Run the full short-circuit chain
                // (Shock → Subtype → Conditional → Unconditional) plus the
                // independent counter / copy binders so the prod card-build
                // path matches the factory path exactly.
                if (!ShockLandBinder.Bind(card, entity, replacements) &&
                    !SubtypeEntersTappedBinder.Bind(card, entity, replacements) &&
                    !ConditionalEntersTappedBinder.Bind(card, entity, replacements))
                {
                    EntersTappedBinder.Bind(card, entity, replacements);
                }
                EntersWithCountersBinder.Bind(card, entity, replacements);
                EntersAsCopyBinder.Bind(card, entity, replacements, effects);
                OracleLandActivatedAbilityBinder.Bind(card, entity, controller);
                OracleLoyaltyAbilityBinder.Bind(card, entity, controller);
                return;
            }
        }

        // Fallback: no repo or unknown card — attach basic-land mana only.
        OracleManaBinder.BindBasicLandMana(card, controller);
    }

    /// <summary>
    /// Kicks off the priority round. Returns immediately; the loop awaits on
    /// the first player's agent. Submit commands via <see cref="SubmitAsync"/>
    /// to advance.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_loopTask != null)
        {
            throw new InvalidOperationException("Game already started.");
        }

        // Capture the signal BEFORE kicking the loop. The loop runs
        // synchronously up to its first agent.Prompt<T> call, which fires
        // PromptRequested → PulsePromptSignal completes the captured TCS.
        var settled = _nextPromptSignal.Task;
        // Single-round path (test-only — production drives StartFullGameAsync).
        // The full-game path installs its per-game registry scope inside
        // GameDriver.RunGameAsync; this path has no driver, so the facade
        // installs the scope itself for the duration of the background loop
        // task (the scope must live on the loop's async flow, NOT this
        // synchronous StartAsync call, so it stays active across every later
        // SubmitAsync continuation that resumes the loop). Concurrent
        // single-round facades therefore get isolated registry stores.
        _loopTask = RunSingleRoundWithScopeAsync(ct);
        await WaitForPromptOrLoopAsync(settled, _loopTask, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Run the single-round priority loop inside this game's ambient registry
    /// scope. Seeds the per-game AgentRegistry store with both seats' effective
    /// agents (so effect closures prompt the right player) and disposes the
    /// scope when the round ends, reclaiming the entries.
    /// </summary>
    private async Task RunSingleRoundWithScopeAsync(CancellationToken ct)
    {
        using var registryScope = Majik.Core.Game.GameRegistryScope.PushForGame();
        AgentRegistry.Set(_alice, _aliceAgentEffective);
        AgentRegistry.Set(_bob, _bobAgentEffective);
        await _loop.RunUntilRoundEndsAsync(_alice, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Run the full <see cref="GameDriver"/> loop. Returns immediately;
    /// the driver runs in the background and reads its decisions from
    /// the two <see cref="RemoteAgent"/>s (so callers drive it via
    /// <see cref="SubmitAsync"/>). Use <see cref="FullGameTask"/> to
    /// await completion.
    ///
    /// Phase 10 entry point — shuffle, mulligan, multi-turn, SBAs,
    /// combat, win conditions all handled by the driver.
    /// </summary>
    public Task StartFullGameAsync(
        int firstPlayerSlot = 0,
        int maxTurns = 30,
        CancellationToken ct = default,
        Majik.Core.Random.GameRandom? rng = null,
        Func<Player, Majik.Core.Game.IAutoPassPrefsView?>? autoPassPrefsProvider = null,
        Func<DateTime>? clock = null,
        Majik.Core.Game.ILogicalClock? logicalClock = null)
    {
        if (_loopTask != null || _fullGameTask != null)
        {
            throw new InvalidOperationException("Game already started.");
        }

        var orderedPlayers = firstPlayerSlot == 1
            ? new[] { _bob, _alice }
            : new[] { _alice, _bob };

        // Slice 5a — server-side auto-pass. Build a per-seat prefs
        // resolver that maps the engine's Player back through the
        // caller-supplied provider. Bot seats: BotPlayerAgent drives
        // itself, so the wired provider should return null for the bot
        // seat (the server's MatchService only writes prefs for the
        // human seat sub). If no provider was passed (legacy tests),
        // every seat returns null → auto-pass disabled (pre-Slice-5a
        // behaviour preserved).
        Func<Player, Majik.Core.Game.IAutoPassPrefsView?>? prefsForLoop = null;
        if (autoPassPrefsProvider != null)
        {
            prefsForLoop = player =>
            {
                // Never auto-pass for a seat whose effective agent isn't
                // a RemoteAgent — that seat is bot-driven and must reach
                // its own ChoosePriorityActionAsync to decide. Defence-
                // in-depth: even if the caller's provider returns prefs
                // for the bot sub, we suppress here.
                var effective = ReferenceEquals(player, _alice) ? _aliceAgentEffective : _bobAgentEffective;
                if (effective is not RemoteAgent) return null;
                return autoPassPrefsProvider(player);
            };
        }

        var driver = new GameDriver(
            players: orderedPlayers,
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = _aliceAgentEffective,
                [_bob] = _bobAgentEffective,
            },
            stack: _stack,
            zoneService: _zones,
            triggerManager: _triggers,
            stackResolver: _resolver,
            stateBasedActions: _sba,
            priorityManager: _priority,
            combatFlow: _combatFlow,
            rng: rng,
            eventBus: _bus,
            spellDefinitionResolver: BuildSpellDefinitionResolver(),
            continuousEffects: ContinuousEffects,
            // CR 305.2 — TurnDriver resets the tracker per turn and the
            // PriorityLoop it builds each round consults the same instance
            // to gate PlayLand actions.
            landDropTracker: LandDrops,
            // Slice 5a — auto-pass plumbing forwarded into every per-round
            // PriorityLoop the TurnDriver builds. PriorityKinds.Build is
            // the single source of truth for "is this a dead window?".
            autoPassPrefsProvider: prefsForLoop,
            isPassOnlyDeadWindow: ctx => PriorityKinds.IsPassOnly(PriorityKinds.Build(ctx)),
            clock: clock,
            // Determinism (PLAN 08 prerequisite): forward a caller-supplied
            // logical clock (replay / determinism harness). Null = the driver
            // mints a fresh per-game LogicalClock.
            logicalClock: logicalClock);

        var settled = _nextPromptSignal.Task;
        // CR 103.2 / 103.4 / 103.7 — the starting player is decided UPSTREAM
        // by the pre-game die roll + the winner's play/draw choice (see
        // MatchService.PlayDrawAsync), which orders the intended first player
        // into orderedPlayers[0] above. Tell the driver to honour that seat
        // (index 0) instead of re-rolling at random; the random flip is only
        // a fallback for callers (bot-vs-bot sims) that don't specify a
        // starting player.
        _fullGameTask = driver.RunGameAsync(maxTurns, startingPlayerIndex: 0, ct: ct);
        return WaitForPromptOrLoopAsync(settled, _fullGameTask, ct);
    }

    /// <summary>Task representing the in-flight full-game run, or null
    /// when only <see cref="StartAsync"/> was called.</summary>
    public Task<GameDriver.GameResult>? FullGameTask => _fullGameTask;

    /// <summary>
    /// Submit a player command. Returns when the engine has processed it
    /// (which may include resolving stack objects and reaching the next
    /// prompt).
    /// </summary>
    public async Task SubmitAsync(GameCommand command, CancellationToken ct = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        var agent = command.PlayerId == _alice.Id ? _aliceAgent
                  : command.PlayerId == _bob.Id ? _bobAgent
                  : throw new InvalidOperationException($"Unknown player {command.PlayerId}.");

        // After a seat-swap the effective agent for that seat is no longer
        // the RemoteAgent — the bot drives itself, so external Submit calls
        // are nonsensical.
        var effective = command.PlayerId == _alice.Id ? _aliceAgentEffective : _bobAgentEffective;
        if (!ReferenceEquals(effective, agent))
            throw new InvalidOperationException("Cannot submit to a bot seat.");

        // Capture the signal BEFORE submitting. agent.Submit resolves the
        // TCS the engine is awaiting; the engine then resumes on the
        // threadpool and (typically) reaches the next agent prompt, which
        // pulses our signal. Awaiting here guarantees the next SubmitAsync
        // sees a registered prompt instead of "Agent has no pending prompt".
        var settled = _nextPromptSignal.Task;

        agent.Submit(command);
        _log.Append(command);

        var loop = _loopTask ?? _fullGameTask;
        await WaitForPromptOrLoopAsync(settled, loop, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for either the next-prompt signal to fire or the active
    /// engine task to complete (whichever comes first). Re-throws if the
    /// engine task ended in a faulted state so the test/HTTP caller sees
    /// the real exception instead of a silent hang on the next submit.
    /// </summary>
    private static async Task WaitForPromptOrLoopAsync(
        Task settled,
        Task? loop,
        CancellationToken ct)
    {
        if (loop == null)
        {
            await settled.WaitAsync(ct).ConfigureAwait(false);
            return;
        }
        var done = await Task.WhenAny(settled, loop).ConfigureAwait(false);
        if (done == loop && loop.IsFaulted)
        {
            await loop.ConfigureAwait(false); // surface the exception
        }
    }

    /// <summary>Append-only log of every command submitted via SubmitAsync.</summary>
    public ActionLog Log => _log;

    /// <summary>
    /// Serializes the current state to JSON bytes — read-only spectator
    /// snapshot. Pair with <see cref="SpectatorSnapshot.Load"/>.
    /// </summary>
    public byte[] Save() => JsonSerializer.SerializeToUtf8Bytes(GetState());

    /// <summary>
    /// Full snapshot including action log. Pair with
    /// <see cref="GameSnapshot"/> deserialization for replay.
    /// </summary>
    public GameSnapshot SaveSnapshot() => new(
        State: GetState(),
        Log: _log.Actions.Select(a => new LoggedCommand(a.At, a.Command)).ToList());

    public byte[] SaveSnapshotBytes() => JsonSerializer.SerializeToUtf8Bytes(SaveSnapshot());

    /// <summary>Full-information snapshot (spectator view). Use
    /// <see cref="GetStateFor"/> for a per-player view that masks
    /// opponent hidden zones.</summary>
    public GameStateDto GetState() => StateSnapshotter.Snapshot(
        GameId, turnNumber: _currentTurn, phase: _currentPhase,
        activePlayer: _currentActivePlayer ?? _priority.CurrentPlayer ?? _alice,
        players: new[] { _alice, _bob },
        stack: _stack,
        turnState: _currentTurnState,
        // PLAN 04 — report the last event's seq; reading does NOT advance it.
        seq: Interlocked.Read(ref _lastEventSeq));

    /// <summary>Per-player snapshot. CR 706 hidden information (opponent
    /// hand) is masked. Pass the requesting player's id; returns null
    /// when the id matches no slot in this game.</summary>
    public GameStateDto? GetStateFor(Guid viewerPlayerId)
    {
        var viewer = ResolveSlot(viewerPlayerId);
        if (viewer == null) return null;

        return StateSnapshotter.Snapshot(
            GameId, turnNumber: _currentTurn, phase: _currentPhase,
            activePlayer: _currentActivePlayer ?? _priority.CurrentPlayer ?? _alice,
            players: new[] { _alice, _bob },
            stack: _stack,
            viewer: viewer,
            turnState: _currentTurnState,
            // PLAN 04 — report the last event's seq; reading does NOT advance it.
            seq: Interlocked.Read(ref _lastEventSeq));
    }

    private Player? ResolveSlot(Guid id)
    {
        if (_alice.Id == id) return _alice;
        if (_bob.Id == id) return _bob;
        return null;
    }

    /// <summary>
    /// Stream events. Returned <see cref="IDisposable"/> unsubscribes.
    /// Receives the public / full-reveal <see cref="EventDto"/>; use
    /// <see cref="SubscribeEnvelopes"/> when you need access to the
    /// per-player masked variants for CR 706 hidden-information events.
    /// </summary>
    public IDisposable Subscribe(Action<EventDto> handler)
    {
        _subscribers.Add(handler);
        return new Subscription(() => _subscribers.Remove(handler));
    }

    /// <summary>
    /// Stream <see cref="EventEnvelope"/>s — same firing cadence as
    /// <see cref="Subscribe(Action{EventDto})"/> but the handler receives
    /// the full envelope (public payload + optional per-player overrides).
    /// Used by <c>MatchFacadeBridge</c> so it can route per-recipient
    /// publishes for events that mask card identity (CR 706).
    /// </summary>
    public IDisposable SubscribeEnvelopes(Action<EventEnvelope> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _envelopeSubscribers.Add(handler);
        return new Subscription(() => _envelopeSubscribers.Remove(handler));
    }

    /// <summary>
    /// Subscribe to prompt envelopes. Fires once each time the engine
    /// transitions to awaiting a command from either player. Returned
    /// <see cref="IDisposable"/> detaches the handler.
    /// </summary>
    public IDisposable SubscribePrompts(Action<PromptDto> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Action<IReadOnlyList<Type>> aliceHandler = kinds => handler(BuildPrompt(_alice, _aliceAgent, kinds));
        Action<IReadOnlyList<Type>> bobHandler = kinds => handler(BuildPrompt(_bob, _bobAgent, kinds));
        _aliceAgent.PromptRequested += aliceHandler;
        _bobAgent.PromptRequested += bobHandler;
        return new Subscription(() =>
        {
            _aliceAgent.PromptRequested -= aliceHandler;
            _bobAgent.PromptRequested -= bobHandler;
        });
    }

    // Forward the agent's per-prompt payload (currently: library-search
    // candidate list + label + full library view,
    // see RemoteAgent.ChooseLibraryPickAsync) onto the wire PromptDto.
    // Read synchronously inside PromptRequested — RemoteAgent stashes
    // PendingPayload before invoking the observer, so the field is populated
    // for the kinds that need it (null otherwise).
    // Privacy: the PromptDto is published only to the seat whose agent fired
    // PromptRequested (per-recipient SignalR routing in MatchFacadeBridge).
    // LibraryView is therefore never visible to the opponent.
    private PromptDto BuildPrompt(Player player, RemoteAgent agent, IReadOnlyList<Type> kinds)
    {
        var payload = agent.PendingPayload;
        return new PromptDto(
            GameId: GameId,
            PlayerId: player.Id,
            ExpectedKinds: kinds.Select(t => t.Name).ToList(),
            Candidates: payload?.Candidates,
            Label: payload?.Label,
            LibraryView: payload?.LibraryView,
            SurveilView: payload?.SurveilView,
            YesNoView: payload?.YesNoView,
            RevealView: payload?.RevealView,
            BottomCount: payload?.BottomCount);
    }

    private void BridgeEvent(GameEvent e)
    {
        var type = e.GetType().Name;
        // Snapshot the currently-tracked turn state for the payload builder.
        // Phase labels are already first-class (PreCombatMain / PostCombatMain)
        // since Slice 3; turn state is carried for TurnStateChangedEvent and
        // other turn-state consumers.
        var turnState = _currentTurnState;

        // PLAN 04 — stamp the seq ONCE per engine event, BEFORE the subscriber
        // fan-out, and record it as _lastEventSeq so a subsequent GetState
        // reports this same value. Every per-player masked variant of this one
        // engine event shares this seq (they already share EventId), so the
        // portal's gap-detect on the public seq never sees a false gap across
        // seats.
        var seq = NextSeq();
        _lastEventSeq = seq;

        var publicDto = new EventDto(
            EventId: e.EventId,
            Type: type,
            At: e.Timestamp,
            Payload: EventPayloadBuilder.Build(e, viewer: null, turnState: turnState),
            Seq: seq);

        IReadOnlyDictionary<Guid, EventDto>? perPlayer = null;
        if (EventPayloadBuilder.RequiresPerViewerMasking(e))
        {
            // Build a payload per seated player. Same EventId / Type /
            // timestamp / Seq on each variant — only the inner payload differs.
            // Spectators receive the public DTO via the legacy
            // Subscribe(Action<EventDto>) channel; the bridge picks up
            // the per-player variants from the envelope and routes them
            // through PublishPerRecipient.
            perPlayer = new Dictionary<Guid, EventDto>
            {
                [_alice.Id] = new EventDto(
                    EventId: e.EventId,
                    Type: type,
                    At: e.Timestamp,
                    Payload: EventPayloadBuilder.Build(e, _alice, turnState),
                    Seq: seq),
                [_bob.Id] = new EventDto(
                    EventId: e.EventId,
                    Type: type,
                    At: e.Timestamp,
                    Payload: EventPayloadBuilder.Build(e, _bob, turnState),
                    Seq: seq),
            };
        }

        var envelope = new EventEnvelope(publicDto, perPlayer);

        foreach (var sub in _subscribers.ToList())
        {
            sub(publicDto);
        }
        foreach (var sub in _envelopeSubscribers.ToList())
        {
            sub(envelope);
        }
    }

    private Player? LookupPlayer(Guid playerId)
    {
        if (_alice.Id == playerId) return _alice;
        if (_bob.Id == playerId) return _bob;
        return null;
    }

    private ICard? LookupCard(Guid instanceId)
    {
        foreach (var player in new[] { _alice, _bob })
        {
            foreach (var zoneType in new[] { ZoneType.Hand, ZoneType.Battlefield, ZoneType.Graveyard, ZoneType.Library, ZoneType.Exile, ZoneType.Stack })
            {
                foreach (var card in player.Zones.GetZone(zoneType).GetCards())
                {
                    if (card.InstanceId == instanceId)
                    {
                        return card;
                    }
                }
            }
        }

        return null;
    }

    // Per-match EventBus error routing. The bus already isolates handlers
    // (a throwing handler doesn't abort delivery to the rest); this just
    // makes the failure VISIBLE instead of dropping it on the floor.
    private static void HandleBusHandlerError(Exception ex, GameEvent @event)
    {
        var sink = OnEventHandlerError;
        if (sink != null)
        {
            sink(ex, @event);
            return;
        }

#if DEBUG
        // No sink wired (typically a unit test that didn't opt into one):
        // fail loud. Rethrow on a background thread so the exception isn't
        // swallowed by the bus's catch — surfaces as an unhandled-exception
        // / test-crash rather than a silent freeze. This deliberately
        // encodes "handler exceptions must not be invisible".
        var captured = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
        ThreadPool.QueueUserWorkItem(_ => captured.Throw());
#endif
        // RELEASE with no sink: bus continues delivery (handlers stay
        // isolated) but we have nowhere to log. Production always wires
        // OnEventHandlerError, so this no-op path is dev/test-only.
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;
        public Subscription(Action dispose) { _dispose = dispose; }
        public void Dispose() => _dispose();
    }

    /// <summary>
    /// Prune this facade's two players from every process-level registry
    /// (agents, RNG, event bus, zone service). Per-player <c>Remove</c>
    /// instead of <c>Clear</c> so concurrent matches keep their own seats.
    /// Idempotent.
    ///
    /// <para>
    /// During a live run the registries are backed by per-game ambient stores
    /// (installed in <c>GameDriver.RunGameAsync</c> / the single-round
    /// <see cref="StartAsync"/> wrapper) that are reclaimed automatically when
    /// the run scope ends. This prune targets the process-wide FALLBACK store,
    /// which the <see cref="GameFacade"/> constructor (and any direct-effect
    /// test that never starts a run) seeds with the two seats' agents — those
    /// entries would otherwise leak for the lifetime of the process. The other
    /// three registries are pruned too for symmetry / defence-in-depth in case
    /// a caller seeded the fallback directly. Called from
    /// <c>GameRegistry.Remove</c> when a finished match is evicted.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        AgentRegistry.Remove(_alice);
        AgentRegistry.Remove(_bob);
        Majik.Core.Random.GameRandomRegistry.Remove(_alice);
        Majik.Core.Random.GameRandomRegistry.Remove(_bob);
        Majik.Core.Events.EventBusRegistry.Remove(_alice);
        Majik.Core.Events.EventBusRegistry.Remove(_bob);
        Majik.Core.Services.ZoneServiceRegistry.Remove(_alice);
        Majik.Core.Services.ZoneServiceRegistry.Remove(_bob);
    }
}
