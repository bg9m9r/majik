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

    // Per-facade snapshot of the routing policy, captured ONCE at construction
    // from the process-wide flag. Every card build for THIS game reads this
    // immutable field rather than re-reading the mutable global per card.
    //
    // Concurrency-determinism fix: BuildDeckCard used to read the global
    // RouteThroughNamedFactories flag PER CARD. A test (or any caller) toggling
    // that process-wide flag while a concurrent game's deck build was in flight
    // made that single build STRADDLE two policies — some cards routed through
    // the named factory (which mints a different NUMBER of object ids than the
    // binder-chain shell), some not — so the build minted a non-deterministic
    // count of ids from the per-game DeterministicIdSource. That desynced the
    // id sequence and broke id-identical replay across concurrent games (the
    // id-only divergence the fuzz harness surfaced: names / zones / P-T / life
    // all still matched, only the InstanceIds differed). Snapshotting the flag
    // per facade makes each game's whole card-build self-consistent and immune
    // to a concurrent toggle, so the per-game id sequence is reproducible
    // regardless of what other games / tests do to the global in parallel.
    // Pin the routing policy per game via the Create(routeThroughNamedFactories:)
    // argument (concurrency-safe kill-switch); never mutate the process-wide
    // static while games are building.
    private bool _routeThroughNamedFactories =
        Majik.Core.CardData.FactoryRouting.RouteThroughNamedFactories;

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

    // Replay (PLAN 08 / Phase 29.x). When true, BridgeEvent still folds the
    // event into the facade's tracked state + seq (so GetState stays correct)
    // but DOES NOT fan the event out to any external subscriber — the
    // historical events of a replayed command log must never re-reach the
    // wire. FromSnapshot flips this on for the fast-forward and back off once
    // the rebuilt facade has caught up to the captured state. See FromSnapshot.
    private volatile bool _suppressEventFanout;

    // The RNG seed this facade was started with (StartFullGameAsync rng.Seed).
    // Captured into GameSnapshot so FromSnapshot can rebuild with the SAME
    // seed. Null until a full game starts; surfaced as 0 in the snapshot.
    private int? _gameSeed;

    // PLAN 08 (body) — a per-game deterministic id source the SERVER pinned at
    // facade-create time (seeded from the match seed) and pushed around the deck-
    // card construction. Stashed here so StartFullGameAsync can forward the SAME
    // instance into the driver, keeping ONE monotonic id counter across both the
    // board build and the run. Without this the driver would start a FRESH
    // seed-derived source (PushIfNone, since the create-time scope is already
    // disposed by the time the game starts on a later HTTP call) and restart the
    // counter — colliding run-minted ids with board ids and breaking the
    // id-identity a rehydrate (which uses ONE scope spanning board+run) needs.
    private Majik.Core.Game.IDeterministicIdSource? _pinnedIdSource;

    /// <summary>
    /// PLAN 08 (body) — record the deterministic id source the caller pushed
    /// around this facade's construction, so <see cref="StartFullGameAsync"/>
    /// continues the SAME counter when the game later starts. No-op contribution
    /// when null (the driver mints its own seed-derived source — today's path).
    /// </summary>
    public void UsePinnedIdSource(Majik.Core.Game.IDeterministicIdSource source)
        => _pinnedIdSource = source;

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
    private StepStateType _currentPhase = StepStateType.PreCombatMain;
    private Player? _currentActivePlayer;
    // Since Slice 3 the phase value already carries the precombat /
    // postcombat distinction (CR 505), so the wire/UI labels need no
    // disambiguation. The turn-level (phase) state is still tracked here,
    // fed by TurnDriver's PhaseStateChangedEvent, for turn-state consumers.
    private PhaseStateType? _currentTurnState;

    public Guid GameId { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// PLAN 08 (body) — re-stamp this facade's <see cref="GameId"/> so a
    /// rehydrated replica serves state under the ORIGINAL game id (the portal
    /// keys its state DTO on it, and the registry / Match doc key on it). The
    /// facade's GameId is the one id deliberately left non-deterministic (it's a
    /// facade-level handle, not carried in <see cref="GameSnapshot"/>), so a
    /// rehydrate mints a fresh one; this aligns it back to the match's gid.
    /// Idempotent; only valid before the facade is handed to live callers.
    /// </summary>
    public void OverrideGameId(Guid gameId) => GameId = gameId;

    public bool IsRoundComplete => _loopTask?.IsCompleted ?? false;

    /// <summary>The Alice/creator-slot player. Exposed so tooling can wire
    /// a non-RemoteAgent (e.g. <see cref="Majik.Bot.BotPlayerAgent"/>) before
    /// the game starts.</summary>
    public Player Alice => _alice;

    /// <summary>The Bob/opponent-slot player. See <see cref="Alice"/>.</summary>
    public Player Bob => _bob;

    /// <summary>
    /// Test-only: the live spell stack for this facade. Exposed as internal so
    /// simulation tests (SandboxParityTests) can pass it to
    /// <see cref="Majik.Core.Simulation.SandboxGame.From"/> when building a
    /// mid-game sandbox from a live facade state.
    /// </summary>
    internal Majik.Core.Stack.Stack LiveStack => _stack;

    /// <summary>
    /// Test-only: the live per-turn state tally. Exposed as internal so
    /// simulation tests (SandboxParityTests) can pass it to
    /// <see cref="Majik.Core.Simulation.SandboxGame.From"/>.
    /// </summary>
    internal Majik.Core.Game.TurnState? LiveTurnState => null; // TurnDriver owns TurnState; facade tracks only PhaseStateType. Pass null — acceptable for sandbox seeding.

    /// <summary>
    /// True when the seat with this id is driven by a HUMAN agent — i.e. its
    /// effective agent is still the wire-facing <see cref="RemoteAgent"/> (a
    /// seat whose prompts come over the wire and are answered by
    /// <see cref="SubmitAsync"/>), as opposed to a bot seat whose
    /// <see cref="IPlayerAgent"/> was swapped to a self-driving
    /// <see cref="Majik.Bot.BotPlayerAgent"/>. Used by the replay loop to decide
    /// whether a prompt consumes a logged command (human) or is answered
    /// in-engine by the seat's own agent (bot — never logged). Unknown ids count
    /// as non-human.
    /// </summary>
    public bool IsHumanSeat(Guid playerId)
    {
        if (playerId == _alice.Id) return ReferenceEquals(_aliceAgentEffective, _aliceAgent);
        if (playerId == _bob.Id) return ReferenceEquals(_bobAgentEffective, _bobAgent);
        return false;
    }

    /// <summary>
    /// The id of the engine's current active player (CR 102.1 / 103.7 —
    /// "the active player is the player whose turn it is"). Tracked from
    /// <see cref="TurnStartedEvent"/>; falls back to the priority holder
    /// and finally to <see cref="Alice"/> before any turn has started so
    /// the server clock and wire contract always have a non-empty id to
    /// DERIVE the clock holder / seat identity from rather than recompute.
    /// </summary>
    public Guid ActivePlayerId => (_currentActivePlayer ?? _priority.CurrentPlayer ?? _alice).Id;

    // Read-only turn/phase context for fault-log diagnostics (e.g. OnEngineErrorAsync).
    // Pure exposure of the existing TurnStarted/StepStarted-fed fields; no behavior change.
    public int CurrentTurn => _currentTurn;
    public StepStateType CurrentPhase => _currentPhase;

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
    public ContinuousEffectsService ContinuousEffects { get; }

    /// <summary>
    /// CR 305.2 — per-turn land-drop counter. Shared between the legacy
    /// single-round <see cref="StartAsync"/> path and the
    /// <see cref="StartFullGameAsync"/> driver loop so both enforce the
    /// one-land-per-turn cap against the same instance. Without this
    /// sharing, the priority loop would have no tracker and bots could
    /// play multiple lands per turn.
    /// </summary>
    public LandDropTracker LandDrops { get; } = new();

    /// <summary>Read-only access to the trigger manager (used by fuzz/diagnostic observers).</summary>
    public TriggerManager Triggers => _triggers;

    /// <summary>Read-only access to the event bus (used by fuzz/diagnostic observers).</summary>
    public EventBus EventBus => _bus;

    private GameFacade(Player alice, Player bob)
    {
        _alice = alice;
        _bob = bob;

        // CR 613 — wire the per-match continuous-effects service to the live
        // EventBus. This drives the CDA memoization-cache invalidation
        // (SubscribeAll bumps generation on every event) AND lets the service
        // PUBLISH its log-only ContinuousEffectAdded/Removed events to the
        // wire bridge. Constructed here (not as a field initializer) because a
        // field initializer cannot reference the _bus instance field.
        ContinuousEffects = new ContinuousEffectsService(_bus);

        // CR 110.2 / 700.6 / 611.2c — give the per-match continuous-effects
        // service the live player roster so a controller-scoped group
        // ability-grant routed through the effects-aware factory overload
        // (Chromatic Lantern, Kataki) enumerates BOTH battlefields and filters
        // by effective controller — picking up a stolen permanent you control
        // but an opponent owns (which lives in the owner's battlefield zone).
        ContinuousEffects.PlayersProvider = () => new[] { _alice, _bob };

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
        _bus.Subscribe<StepStartedEvent>(e => { _currentPhase = e.StepType; });
        _bus.Subscribe<PhaseStateChangedEvent>(e => { _currentTurnState = e.CurrentState; });
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

        async Task<bool> DispatchCast(Player actor, PriorityAction.CastSpell cast, GameContext ctx)
        {
            var isPermanent = cast.Card.HasType(Majik.Core.Cards.Types.CardType.Creature)
                || cast.Card.HasType(Majik.Core.Cards.Types.CardType.Artifact)
                || cast.Card.HasType(Majik.Core.Cards.Types.CardType.Enchantment)
                || cast.Card.HasType(Majik.Core.Cards.Types.CardType.Planeswalker);
            if (!isPermanent)
            {
                // No spell-def resolver in this facade — skip non-permanent
                // casts rather than waste the card.
                return false; // not committed: non-permanent with no resolver
            }

            var agent = agents[actor];
            // CR 117.7 / 601.2f — pass the live player roster so the
            // three-arg overload folds in SpellCostIncreaseAbility riders
            // (Sphere of Resistance, Trinisphere, Thalia, Damping Sphere)
            // at cast-time, matching TurnDriver.PriorityRound.
            var cost = cast.AlternativeCost?.AlternativeManaCost
                ?? Majik.Core.Costs.CostReduction.GetEffectiveCost(cast.Card, actor, ctx.AllPlayers);

            // CR 601.2b / 601.2e / 601.2f — variable-X permanent spell (Walking
            // Ballista, Hangarback Walker, Endless One): announce X NOW, BEFORE
            // the mana-source prompt + payment below, and fold it into the
            // dispatcher's cost. Mirrors the already-fixed
            // TurnDriver.DispatchCast twin (keep-in-sync invariant): pre-fix this
            // facade path prompted/paid the printed cost with X folded to generic
            // 0, while SpellCastFlow.CastAsync computed the X-inclusive totalCost
            // AFTER the prompt and the payManaCost callback paid the X-free cost —
            // so an X-spell cast through this loop underpaid (X effectively free).
            // The card's effective ManaCost carries the {X} pip (HasX), so we key
            // off that rather than a SpellDefinition flag (this facade path uses a
            // Vanilla def). The chosen X is forwarded to CastAsync (preChosenX) so
            // the flow reuses it (no double-prompt) and threads it into the
            // resolving effect (ChosenSpellParams.X) + ETB-with-X (PendingCastX).
            int? chosenX = null;
            if (cast.AlternativeCost == null && cost.HasX)
            {
                chosenX = await agent.ChooseXAsync(ctx, cast.Card, CancellationToken.None);
                if (chosenX is { } xv && xv > 0)
                {
                    cost = cost.AddGenericCost(xv);
                }
            }

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
                    // CR 601.2 / CR 727 — player explicitly cancelled. The cast
                    // is abandoned but priority is NOT forced-passed (the human
                    // made a deliberate choice; they remain the priority holder so
                    // they can choose again). Return true so PriorityLoop keeps
                    // the current player rather than calling PassPriority().
                    return true;
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
            try
            {
                var def = Majik.Core.Game.SpellDefinition.Vanilla(_ => Array.Empty<IEffect>());
                // CR 601.2c / 601.2h / CR 732.1 — the mana payment runs INSIDE
                // the cast flow, after target collection, via the payManaCost
                // callback (mirrors TurnDriver.DispatchCast). A cast that
                // becomes illegal earlier in the flow throws before any mana
                // is paid or any source is tapped — nothing to rewind.
                await castFlow.CastAsync(
                    actor, cast.Card, def, agent, ctx, CancellationToken.None,
                    additionalCosts: cast.AdditionalCosts,
                    alternativeCost: cast.AlternativeCost,
                    preChosenMana: payment,
                    payManaCost: _ => manaResolver.Pay(actor, cost, payment),
                    preChosenX: chosenX);
            }
            catch (InvalidOperationException)
            {
                // Swallow — same posture as TurnDriver. Failure leaves the
                // card in hand; bot/agent memo prevents re-proposing.
                return false; // not committed: CastAsync threw
            }
            return true; // committed: spell put on the stack
        }

        async Task DispatchActivate(Player actor, PriorityAction.ActivateAbility activate, GameContext ctx)
        {
            var targets = new List<Majik.Core.Targeting.ITarget>();
            if (activate.Ability is Majik.Core.Abilities.ActivatedAbility aa)
            {
                // CR 602.2b — collect the chosen targets per TargetRequest AND
                // record them on the source ability via SetChosenTargets so the
                // resolution-time effect closures (which read aa.ChosenTargets)
                // see them. AbilityActivator copies sourceAbility.ChosenTargets
                // onto the stack object, but the effect closures captured the
                // SOURCE ability — without this stamp the chosen target was
                // dropped (e.g. Yawgmoth's "-1/-1 on up to one target" never
                // placed a counter). Mirrors DispatchLoyalty / TurnDriver.
                var chosenTargets = new List<IReadOnlyList<object>>();
                foreach (var req in aa.TargetRequests)
                {
                    // Resolve any lazy CandidateGatherer against the live ctx so
                    // the prompt ships the legal candidate pool (the portal
                    // renders only legal targets); mirrors TurnDriver /
                    // SpellCastFlow / TriggerManager.
                    var live = req.ResolveCandidates(ctx);
                    var promptReq = ReferenceEquals(live, req.LegalCandidates)
                        ? req
                        : req.WithCandidates(live);
                    // Name the source card/ability on the prompt label so the
                    // player knows what they're choosing a target FOR (the bare
                    // "up to one target creature" gave no clue which permanent
                    // raised the prompt). Label-only — targeting semantics
                    // unchanged. Mirrors TurnDriver.DispatchActivate.
                    promptReq = promptReq.WithSourceLabel(aa.Source);
                    var chosen = await agents[actor].ChooseTargetsAsync(ctx, promptReq, ct: default);
                    chosenTargets.Add(chosen);
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
                if (chosenTargets.Count > 0)
                {
                    aa.SetChosenTargets(chosenTargets);
                }

                // CR 601.2d / CR 119.4 — divide-damage announcement for a "deals
                // N damage divided as you choose among …" ACTIVATED ability.
                // Prompt the activating player's agent for the per-target split
                // right after targets are chosen; recorded on the source ability,
                // mirrored onto the stack copy by AbilityActivator, threaded into
                // ResolutionContext.DamageDivision at resolve time. Even-split
                // fallback when no spec / empty slot. Mirrors TurnDriver.
                if (aa.DamageDivision is { } divSpec
                    && aa.Source is Majik.Core.Cards.ICard divSource)
                {
                    var slot = divSpec.TargetSlotIndex;
                    var divTargets = slot >= 0 && slot < chosenTargets.Count
                        ? chosenTargets[slot]
                        : (IReadOnlyList<object>)Array.Empty<object>();
                    var division = await Majik.Core.Players.Agents.DamageDivisionDefaults
                        .PromptAsync(agents[actor], ctx, divSource, divSpec.TotalDamage, divTargets, ct: default)
                        .ConfigureAwait(false);
                    aa.SetChosenDamageDivision(division);
                }
            }

            // CR 700.6 / 701.17 — if a "Sacrifice another creature" / "Sacrifice
            // a creature" COST is on the ability, prompt the controller to choose
            // WHICH creature to sacrifice (the same ChooseAsync prompt the portal
            // renders) and stamp it onto the cost BEFORE payment. Without this the
            // cost silently auto-picks the first creature (live-play bug).
            await Majik.Core.Game.SacrificeCostPrompt.ChooseSacrificesAsync(
                actor, activate.Ability, agents[actor], ctx);

            // GAP 2 — variable-X cost (CR 601.2e analogue). Mirrors
            // TurnDriver.DispatchActivate: when the ability's mana cost contains
            // {X} (ManaCost.HasX), prompt the agent for X, record it on the
            // source ability (ResolutionContext.ChosenX, mirrored onto the stack
            // copy by AbilityActivator), and expand {X} → X generic in the costs
            // via the spell path's ManaCost.AddGenericCost machinery. A non-X
            // ability pays its printed costs unchanged.
            var costsToPay = activate.Ability.Costs;
            if (activate.Ability is Majik.Core.Abilities.ActivatedAbility aaForX
                && Majik.Core.Costs.VariableXCostExpansion.HasVariableX(activate.Ability.Costs)
                && activate.Ability.Source is Majik.Core.Cards.ICard xSource)
            {
                var x = await agents[actor].ChooseXAsync(ctx, xSource, ct: default);
                aaForX.SetChosenX(x);
                costsToPay = Majik.Core.Costs.VariableXCostExpansion.Expand(
                    activate.Ability.Costs, x);
            }

            var activator = new Majik.Core.Services.AbilityActivator(_stack, _bus);
            try
            {
                activator.ActivateAbility(activate.Ability, actor, targets, costsToPay);
            }
            catch (InvalidOperationException)
            {
                // Cost-payment or zone-gate failed — let the priority loop move on.
            }
        }

        async Task DispatchLoyalty(Player actor, PriorityAction.ActivateLoyaltyAbility activate, GameContext ctx)
        {
            // CR 606.3 — activate a planeswalker loyalty ability. Mirrors
            // TurnDriver.DispatchLoyalty (Majik.Core/Game/TurnDriver.cs) so the
            // legacy single-round StartAsync path accepts
            // ActivateLoyaltyAbilityCommand instead of throwing
            // "PriorityLoop received ActivateLoyaltyAbility but no loyaltyDispatcher".
            // Loyalty abilities are sorcery-speed (active player + main phase +
            // empty stack) and once-per-turn; re-verify here so a stale / out-
            // of-window proposal is swallowed rather than mutating state.
            var loyalty = activate.Ability;
            if (!loyalty.CanActivate()) return;
            var inSorceryWindow = ReferenceEquals(ctx.ActivePlayer, actor)
                && ctx.CurrentPhase is { } phase && phase.IsMain()
                && ctx.Stack.Count == 0
                && ReferenceEquals(loyalty.Source.Controller, actor);
            if (!inSorceryWindow) return;

            // CR 602.2b — collect targets from the loyalty ability's
            // TargetRequests via the activating player's agent.
            var chosenTargets = new List<IReadOnlyList<object>>();
            var targetWrappers = new List<Majik.Core.Targeting.ITarget>();
            foreach (var req in loyalty.TargetRequests)
            {
                var live = req.ResolveCandidates(ctx);
                var promptReq = ReferenceEquals(live, req.LegalCandidates)
                    ? req
                    : req.WithCandidates(live);
                var chosen = await agents[actor].ChooseTargetsAsync(ctx, promptReq, ct: default);
                chosenTargets.Add(chosen);
                foreach (var obj in chosen)
                {
                    Majik.Core.Targeting.ITarget? wrapper = obj switch
                    {
                        Majik.Core.Cards.Permanent perm => Majik.Core.Targeting.Target.Permanent(perm),
                        Majik.Core.Cards.ICard card => Majik.Core.Targeting.Target.Card(card),
                        Player p => Majik.Core.Targeting.Target.Player(p),
                        Majik.Core.Spells.ISpell spell => Majik.Core.Targeting.Target.Spell(spell),
                        Majik.Core.Abilities.IActivatedAbility ab => Majik.Core.Targeting.Target.Ability(ab),
                        _ => null,
                    };
                    if (wrapper != null) targetWrappers.Add(wrapper);
                }
            }

            // CR 606.3/606.5 — pay the loyalty cost as the ability is put on the
            // stack (add/remove loyalty + mark once-per-turn).
            try
            {
                loyalty.PayLoyaltyCost();
            }
            catch (InvalidOperationException)
            {
                return; // raced out of the activation window — no state change.
            }

            // Build the ActivatedAbility stack object from the loyalty template
            // (loyalty cost pre-paid; effects resolve later off the stack).
            var stackObject = new Majik.Core.Abilities.ActivatedAbility(
                source: loyalty.Source,
                controller: actor,
                targets: targetWrappers.Count > 0 ? targetWrappers : null,
                costs: null,
                effects: loyalty.Effects,
                targetRequests: loyalty.TargetRequests.Count > 0 ? loyalty.TargetRequests : null,
                sorcerySpeed: true,
                damageDivision: loyalty.DamageDivision);
            if (chosenTargets.Count > 0)
            {
                stackObject.SetChosenTargets(chosenTargets);
            }

            // CR 601.2d / CR 119.4 — divide-damage announcement for a loyalty
            // ability dealing N damage divided among its chosen targets. Mirrors
            // TurnDriver.DispatchLoyalty / GameFacade.DispatchActivate.
            if (loyalty.DamageDivision is { } loyaltyDivSpec
                && loyalty.Source is Majik.Core.Cards.ICard loyaltyDivSource)
            {
                var slot = loyaltyDivSpec.TargetSlotIndex;
                var divTargets = slot >= 0 && slot < chosenTargets.Count
                    ? chosenTargets[slot]
                    : (IReadOnlyList<object>)Array.Empty<object>();
                var division = await Majik.Core.Players.Agents.DamageDivisionDefaults
                    .PromptAsync(agents[actor], ctx, loyaltyDivSource, loyaltyDivSpec.TotalDamage, divTargets, ct: default)
                    .ConfigureAwait(false);
                stackObject.SetChosenDamageDivision(division);
            }

            _stack.Push(stackObject);
            _bus.Publish(new Majik.Core.Domain.DomainEvents.AbilityActivatedEvent(stackObject));
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
            phaseAccessor: () => StepStateType.PreCombatMain,
            // CR 305.2 — share the facade's per-turn land-drop counter so
            // PlayLandCommand submissions are gated on the same instance
            // a subsequent StartFullGameAsync run also uses.
            landDropTracker: LandDrops,
            castDispatcher: DispatchCast,
            activateDispatcher: DispatchActivate,
            loyaltyDispatcher: DispatchLoyalty,
            manaAbilityDispatcher: DispatchManaAbility,
            // CR 704.1 — check SBAs before priority + after each resolution on
            // the legacy single-round StartAsync path too, so a 0/0 dies
            // immediately rather than lingering. Mirrors the TurnDriver wiring.
            checkStateBasedActions: () => _sba.CheckStateBasedActions(
                new[] { _alice, _bob },
                new[] { _alice, _bob }.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList()));
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
    ///
    /// <para>Thin delegation to the shared
    /// <see cref="SpellDefinitionResolverFactory"/> so
    /// <see cref="Majik.Core.Simulation.SandboxGame"/> builds the exact same
    /// resolver shape over its sandbox-local subsystems.</para>
    /// </summary>
    private Func<ICard, Player, Majik.Core.Stack.Stack?, SpellDefinition?>? BuildSpellDefinitionResolver()
        => SpellDefinitionResolverFactory.Create(
            _cardRepo,
            replacements: Replacements,
            effects: ContinuousEffects,
            triggers: _triggers,
            eventBus: _bus,
            zones: _zones);

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
        ReplacementBus? replacements = null,
        bool? routeThroughNamedFactories = null)
    {
        var alice = new Player(aliceName, 20);
        var bob = new Player(bobName, 20);

        // Construct the facade first so its internal ReplacementBus is available.
        // Any externally-supplied bus is ignored — binders register onto the
        // facade's own bus, which ZoneService already holds a reference to.
        var facade = new GameFacade(alice, bob);
        // Per-game routing policy: an explicit argument pins it for THIS game
        // (concurrency-safe kill-switch — no process-wide mutation); null keeps
        // the snapshot the facade captured from the global flag at construction.
        if (routeThroughNamedFactories is { } route0)
        {
            facade._routeThroughNamedFactories = route0;
        }
        var bus = facade.Replacements;

        // Stash the repo so StartFullGameAsync can build a spell-definition
        // resolver later. Without this, TurnDriver.DispatchCast falls through
        // to "no SpellDef for instant/sorcery" and silently rotates the card
        // back into hand — every non-permanent spell becomes uncastable.
        facade._cardRepo = cardRepo;

        // Snapshot the routing policy ONCE for this whole deck build so a
        // concurrent toggle of the process-wide flag can't make this build
        // straddle two policies (which would perturb the per-game id sequence).
        var route = facade._routeThroughNamedFactories;
        foreach (var card in aliceDeck)
        {
            var live = BuildDeckCard(card, alice, cardRepo, bus, facade.ContinuousEffects,
                route, facade._triggers, facade._zones, facade._bus);
            alice.Zones.GetZone(ZoneType.Library).AddCard(live);
        }
        foreach (var card in bobDeck)
        {
            var live = BuildDeckCard(card, bob, cardRepo, bus, facade.ContinuousEffects,
                route, facade._triggers, facade._zones, facade._bus);
            bob.Zones.GetZone(ZoneType.Library).AddCard(live);
        }

        return facade;
    }

    /// <summary>
    /// CR 100.4 / CR 408 — populate <paramref name="seat"/>'s sideboard
    /// (a.k.a. the wishboard) with live card instances built from
    /// <paramref name="sideboard"/>. Each shell runs through the SAME
    /// <see cref="BuildDeckCard"/> binder/factory path the main deck uses, so
    /// a wish-tutor effect ("a card you own from outside the game" — Wish,
    /// Burning Wish, Karn, the Great Creator, etc.) sees a fully-bound card,
    /// and a nominated companion can be cast from this zone.
    ///
    /// <para>The seat must be one of this facade's two players (<see cref="Alice"/>
    /// or <see cref="Bob"/>). A null / empty list is a no-op — archetypes
    /// without a defined sideboard simply get an empty wishboard, exactly as
    /// before. Call before <see cref="StartFullGameAsync"/>; the cards land in
    /// the <see cref="ZoneType.Sideboard"/> zone, never in the opening library.</para>
    /// </summary>
    public void PopulateSideboard(Player seat, IReadOnlyList<ICard> sideboard)
    {
        ArgumentNullException.ThrowIfNull(seat);
        if (!ReferenceEquals(seat, _alice) && !ReferenceEquals(seat, _bob))
        {
            throw new ArgumentException(
                "Sideboard can only be populated for one of this facade's two seats.",
                nameof(seat));
        }
        if (sideboard == null || sideboard.Count == 0) return;

        var route = _routeThroughNamedFactories;
        foreach (var card in sideboard)
        {
            var live = BuildDeckCard(card, seat, _cardRepo, Replacements, ContinuousEffects, route);
            live.SetZone(ZoneType.Sideboard);
            seat.Zones.Sideboard.AddCard(live);
        }
    }

    /// <summary>
    /// Produce the live deck-card instance for <paramref name="shell"/>.
    /// Thin delegate to <see cref="Majik.Core.CardData.DeckCardBuilder.BuildFromShell"/> —
    /// THE single definition of "build a real deck card" (named-factory
    /// routing + binder chain + owner/service wiring), shared with the bot's
    /// determinization sampler so sampled cards are built exactly like
    /// live-deck cards. The logic was extracted verbatim from this class; see
    /// the builder for the full routed-vs-binder-chain documentation.
    /// </summary>
    private static ICard BuildDeckCard(
        ICard shell,
        Player owner,
        ICardRepository? cardRepo,
        ReplacementBus replacements,
        ContinuousEffectsService effects,
        bool routeThroughNamedFactories,
        TriggerManager? triggers = null,
        ZoneService? zones = null,
        IEventBus? eventBus = null)
        => Majik.Core.CardData.DeckCardBuilder.BuildFromShell(
            shell: shell,
            owner: owner,
            cardRepo: cardRepo,
            replacements: replacements,
            effects: effects,
            routeThroughNamedFactories: routeThroughNamedFactories,
            triggers: triggers,
            zones: zones,
            eventBus: eventBus);
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
        // CR 102.4 — register the live seats so mana-ability "each opponent"
        // riders (Grove of the Burnwillows) resolve the opponent set at
        // activation on the single-round path too (mirrors GameDriver).
        Majik.Core.Game.GamePlayersRegistry.Set(new[] { _alice, _bob });
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
        Majik.Core.Game.ILogicalClock? logicalClock = null,
        Majik.Core.Game.IDeterministicIdSource? idSource = null)
    {
        if (_loopTask != null || _fullGameTask != null)
        {
            throw new InvalidOperationException("Game already started.");
        }

        // PLAN 08 / Phase 29.x — remember the seed so SaveSnapshot can capture
        // it and FromSnapshot can rebuild deterministically. When no rng is
        // supplied the driver mints its own random seed (non-reproducible);
        // record 0 (unknown) in that case.
        _gameSeed = rng?.Seed;

        // PLAN 08 (body) — if the caller didn't pass an id source but pinned one
        // at construction (server create-under-id-scope path), forward THAT same
        // instance so the run continues the board build's monotonic id counter
        // (id-identity across board+run). When neither is set the driver seeds
        // its own from the rng seed — today's behaviour.
        idSource ??= _pinnedIdSource;

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
            logicalClock: logicalClock,
            // PLAN 08 — forward a caller-supplied deterministic id source. Null =
            // the driver seeds one from the game RNG seed, so replay (rebuilt with
            // GameSnapshot.Seed) mints the SAME id sequence → id-identical replay.
            idSource: idSource);

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
    /// Full snapshot including action log + the deterministic-replay inputs
    /// (RNG seed, seat ids, instance-id → card-name map). Pair with
    /// <see cref="FromSnapshot"/> to fast-forward a fresh facade to this state.
    /// </summary>
    public GameSnapshot SaveSnapshot() => new(
        State: GetState(),
        Log: _log.Actions.Select(a => new LoggedCommand(a.At, a.Command)).ToList(),
        Seed: _gameSeed ?? 0,
        AliceId: _alice.Id,
        BobId: _bob.Id,
        InstanceNames: BuildInstanceNameMap());

    /// <summary>
    /// Map every live card instance id → its name, so <see cref="FromSnapshot"/>
    /// can translate the action log's id-bearing commands onto the rebuilt
    /// facade's freshly-minted cards by name + position (the verbatim ids
    /// don't survive the rebuild — deferred id-reseeding). We snapshot the
    /// whole board, not just ids that appear in the log, so a later id-bearing
    /// command always resolves.
    /// </summary>
    private IReadOnlyDictionary<Guid, string> BuildInstanceNameMap()
    {
        var map = new Dictionary<Guid, string>();
        foreach (var player in new[] { _alice, _bob })
        {
            foreach (var zoneType in new[]
                     {
                         ZoneType.Hand, ZoneType.Battlefield, ZoneType.Graveyard,
                         ZoneType.Library, ZoneType.Exile, ZoneType.Stack,
                     })
            {
                foreach (var card in player.Zones.GetZone(zoneType).GetCards())
                {
                    map[card.InstanceId] = card.Name;
                }
            }
        }
        return map;
    }

    public byte[] SaveSnapshotBytes() => JsonSerializer.SerializeToUtf8Bytes(SaveSnapshot());

    /// <summary>
    /// Replay-from-command-log (PLAN 08 / Phase 29.x). Rebuild a fresh facade
    /// from <paramref name="snapshot"/> and fast-forward it to the captured
    /// state by re-applying every logged command, with event fan-out
    /// SUPPRESSED so the historical events never re-reach a subscriber.
    ///
    /// <para>The fresh facade is produced by <paramref name="buildFreshFacade"/>
    /// — the caller's own deck/board-seeding code, reused verbatim so the
    /// initial position matches. The game is then started with the SAME
    /// <see cref="GameSnapshot.Seed"/> + a FRESH <see cref="LogicalClock"/>, so
    /// shuffles, draws, and order-determining timestamps replay identically.
    /// As each prompt arrives we pop the next logged command, rebind its
    /// nondeterministic ids (seat + card-by-name) onto the rebuilt facade's
    /// live objects, and submit it through the real <see cref="SubmitAsync"/>
    /// path. Suppression is lifted once the log is exhausted, so the returned
    /// facade is a normal LIVE facade (its subscribers simply saw none of the
    /// replayed history).</para>
    ///
    /// <para>Replay is STRUCTURAL, not id-identical — the rebuilt facade mints
    /// new Guids. Full id-identical replay (client-facing rehydration) needs
    /// the deferred id-reseeding step.</para>
    /// </summary>
    /// <param name="snapshot">The saved game (seed + ordered command log).</param>
    /// <param name="buildFreshFacade">Builds a fresh facade with the SAME
    /// initial board/decks the original snapshot started from.</param>
    /// <param name="onFacadeCreated">Optional hook invoked the instant the
    /// fresh facade exists, BEFORE replay starts — used by tests to attach a
    /// subscriber and prove fan-out suppression holds across the fast-forward.</param>
    public static async Task<GameFacade> FromSnapshot(
        GameSnapshot snapshot,
        Func<GameFacade> buildFreshFacade,
        Action<GameFacade>? onFacadeCreated = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(buildFreshFacade);

        return await ReplayInternalAsync(
            buildFreshFacade,
            seed: snapshot.Seed,
            aliceId: snapshot.AliceId,
            bobId: snapshot.BobId,
            instanceNames: snapshot.InstanceNames,
            log: snapshot.Log,
            onFacadeCreated: onFacadeCreated,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// PLAN 08 (body) — replica rehydration entry point. Reconstructs a LIVE
    /// <see cref="GameFacade"/> from the pinned game <paramref name="seed"/> + the
    /// ordered command log (<paramref name="commandsSinceCheckpoint"/> — the whole
    /// log when there is no checkpoint, or the checkpoint's bundled prefix
    /// concatenated with the commands issued since), reaching the live state by
    /// re-applying every command through the real <see cref="SubmitAsync"/> path
    /// with event fan-out SUPPRESSED across the replay (the bridge attaches only
    /// AFTER replay completes, so a rehydrated facade never re-emits history).
    ///
    /// <para>Unlike <see cref="FromSnapshot"/>, this OWNS the per-game
    /// <see cref="DeterministicIdScope"/> (seeded from <paramref name="seed"/>),
    /// installing it around BOTH <paramref name="buildFreshFacade"/> (initial
    /// board construction) AND the replay. With the same seed + same command
    /// order, every portal-facing id (<c>Player.Id</c>, <c>Card.InstanceId</c>,
    /// stack ids) is reproduced byte-for-byte — the property the portal reducer
    /// needs for a crashed game to resume seamlessly on another replica.</para>
    ///
    /// <para>Because the rebuild is id-identical, the logged commands' verbatim
    /// ids already match the rebuilt facade's live objects — id rebinding is a
    /// no-op safety net rather than a correctness requirement.</para>
    /// </summary>
    /// <param name="buildFreshFacade">Builds a fresh facade with the SAME initial
    /// board/decks the original started from (deck composition is reconstructable
    /// from the persisted match seed + deck snapshot).</param>
    /// <param name="seed">The pinned per-game RNG seed (<c>Match.GameSeed</c>).</param>
    /// <param name="commandsSinceCheckpoint">The ordered command log to replay.</param>
    /// <param name="onFacadeCreated">Optional hook invoked the instant the fresh
    /// facade exists, BEFORE replay starts (tests attach a subscriber to prove
    /// fan-out suppression holds).</param>
    public static async Task<GameFacade> Rehydrate(
        Func<GameFacade> buildFreshFacade,
        int seed,
        IReadOnlyList<LoggedCommand> commandsSinceCheckpoint,
        Action<GameFacade>? onFacadeCreated = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(buildFreshFacade);
        ArgumentNullException.ThrowIfNull(commandsSinceCheckpoint);

        // OWN the id scope so the caller doesn't have to: install a per-game
        // deterministic id source seeded from the game seed around BOTH the
        // board build and the replay. The driver continues this source via
        // PushIfNone, so run-minted ids continue the same monotonic sequence —
        // the rehydrated facade comes out id-identical to the original.
        using var idScope = DeterministicIdScope.Push(new DeterministicIdSource(seed));

        return await ReplayInternalAsync(
            buildFreshFacade,
            seed: seed,
            aliceId: default,
            bobId: default,
            instanceNames: null,
            log: commandsSinceCheckpoint,
            onFacadeCreated: onFacadeCreated,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared replay engine behind <see cref="FromSnapshot"/> and
    /// <see cref="Rehydrate"/>: build a fresh facade, start it with the given
    /// seed + a fresh logical clock, and fast-forward by re-applying every logged
    /// command (rebinding nondeterministic ids when a seat/name map is supplied —
    /// a no-op under an id-identical rehydrate) with event fan-out suppressed.
    /// </summary>
    private static async Task<GameFacade> ReplayInternalAsync(
        Func<GameFacade> buildFreshFacade,
        int seed,
        Guid aliceId,
        Guid bobId,
        IReadOnlyDictionary<Guid, string>? instanceNames,
        IReadOnlyList<LoggedCommand> log,
        Action<GameFacade>? onFacadeCreated,
        CancellationToken ct)
    {
        var facade = buildFreshFacade();
        onFacadeCreated?.Invoke(facade);

        // Map the snapshot's original seat ids onto the rebuilt facade's
        // freshly-minted seats so each logged command's PlayerId rebinds.
        var seatById = new Dictionary<Guid, Player>();
        if (aliceId != default) seatById[aliceId] = facade._alice;
        if (bobId != default) seatById[bobId] = facade._bob;

        var names = instanceNames ?? new Dictionary<Guid, string>();

        // Fan-out suppression for the whole fast-forward. Folding into state
        // still happens (GetState stays correct); only external broadcast is
        // gated.
        facade._suppressEventFanout = true;
        try
        {
            var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
            using var sub = facade.SubscribePrompts(p => channel.Writer.TryWrite(p));

            await facade.StartFullGameAsync(
                maxTurns: 30,
                rng: new Majik.Core.Random.GameRandom(seed),
                logicalClock: new LogicalClock(),
                ct: ct).ConfigureAwait(false);

            var game = facade._fullGameTask!;
            var pending = new Queue<LoggedCommand>(log);

            // Prompt-driven replay: the rebuilt game raises prompts in the
            // SAME order as the original (same seed). The original command log
            // is 1:1 with the original's HUMAN prompts only — bot seats answer
            // in-engine via IPlayerAgent.ChooseAsync and are NEVER logged
            // (SubmitAsync rejects bot seats, so RecordCommand only fires on
            // accepted human commands). For a vs-bot match the rehydrated facade
            // re-installs the deterministic BotPlayerAgent (same archetype +
            // seed), so the bot seat re-makes the SAME decisions in-engine and
            // its prompts never reach this channel (they don't go through the
            // RemoteAgent.PromptRequested path SubscribePrompts listens on). The
            // IsHumanSeat guard below is the explicit contract: only HUMAN
            // prompts consume a logged command; any non-human prompt that
            // somehow surfaces is answered by its own agent, not by dequeuing a
            // human command (which would desync the human log against the wrong
            // prompt).
            while (pending.Count > 0)
            {
                if (game.IsCompleted) break;

                var read = channel.Reader.WaitToReadAsync(ct).AsTask();
                var winner = await Task.WhenAny(read, game).ConfigureAwait(false);
                if (winner == game) break;
                if (!await read.ConfigureAwait(false)) break;
                if (!channel.Reader.TryRead(out var prompt)) continue;

                // Bot-seat prompt (defensive — normally never reaches here):
                // do NOT consume a logged human command; the seat's own
                // re-installed agent answers it in-engine.
                if (!facade.IsHumanSeat(prompt.PlayerId)) continue;

                var logged = pending.Dequeue();
                var rebound = RebindForReplay(
                    facade, logged.Command, prompt, seatById, names);

                try
                {
                    await facade.SubmitAsync(rebound, ct).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // A closed/bot seat or already-finished game — stop
                    // replaying; the state fold has already advanced as far as
                    // the engine accepted.
                    break;
                }
            }
        }
        finally
        {
            facade._suppressEventFanout = false;
        }

        return facade;
    }

    /// <summary>
    /// Rebind a logged command onto the rebuilt facade's live objects: its
    /// <see cref="GameCommand.PlayerId"/> is set to the seat the current prompt
    /// targets (authoritative — the engine is asking THAT seat), and every
    /// card instance id it carries is translated old-id → name (via
    /// <paramref name="instanceNames"/>) → the rebuilt facade's matching live
    /// card. Defender/player ids in combat declarations rebind through the
    /// seat map. Ids that don't resolve are passed through unchanged (the
    /// engine validates and the worst case is a skipped action — structural
    /// equivalence is the bar, not verbatim ids).
    /// </summary>
    private static GameCommand RebindForReplay(
        GameFacade facade,
        GameCommand command,
        PromptDto prompt,
        IReadOnlyDictionary<Guid, Player> seatById,
        IReadOnlyDictionary<Guid, string> instanceNames)
    {
        // PlayerId: trust the prompt — the engine is asking this exact seat.
        var withSeat = command with { PlayerId = prompt.PlayerId };

        Guid Card(Guid oldId)
        {
            if (oldId == default) return oldId;
            if (!instanceNames.TryGetValue(oldId, out var name)) return oldId;
            return facade.ResolveLiveCardByName(name, oldId, instanceNames) ?? oldId;
        }

        Guid PlayerId(Guid oldId) =>
            seatById.TryGetValue(oldId, out var p) ? p.Id : oldId;

        IReadOnlyList<Guid> Cards(IReadOnlyList<Guid> ids) =>
            ids.Select(Card).ToList();

        return withSeat switch
        {
            PlayLandCommand c => c with { LandInstanceId = Card(c.LandInstanceId) },
            CastSpellCommand c => c with
            {
                CardInstanceId = Card(c.CardInstanceId),
                TargetInstanceIds = Cards(c.TargetInstanceIds),
            },
            ChooseTargetsCommand c => c with { TargetInstanceIds = Cards(c.TargetInstanceIds) },
            ChooseManaCommand c => c with { SourceInstanceIds = Cards(c.SourceInstanceIds) },
            ActivateManaAbilityCommand c => c with { PermanentInstanceId = Card(c.PermanentInstanceId) },
            ActivateAbilityCommand c => c with { PermanentInstanceId = Card(c.PermanentInstanceId) },
            ChooseCardsToBottomCommand c => c with { CardInstanceIds = Cards(c.CardInstanceIds) },
            ChooseLibraryPickCommand c => c with
            {
                SelectedInstanceId = c.SelectedInstanceId is { } id ? Card(id) : null,
            },
            ChooseSurveilCommand c => c with
            {
                ToGraveyardInstanceIds = Cards(c.ToGraveyardInstanceIds),
                TopOrderInstanceIds = Cards(c.TopOrderInstanceIds),
            },
            ChooseScryCommand c => c with
            {
                ToBottomInstanceIds = Cards(c.ToBottomInstanceIds),
                TopOrderInstanceIds = Cards(c.TopOrderInstanceIds),
            },
            ChooseFromRevealedCommand c => c with
            {
                InstanceId = c.InstanceId is { } id ? Card(id) : null,
            },
            ChoiceCommand c => c with { SelectedInstanceIds = Cards(c.SelectedInstanceIds) },
            DeclareAttackersCommand c => c with
            {
                Attackers = c.Attackers
                    .Select(a => new AttackerDeclarationDto(Card(a.AttackerInstanceId), PlayerId(a.DefenderId)))
                    .ToList(),
            },
            DeclareBlockersCommand c => c with
            {
                Blockers = c.Blockers
                    .Select(b => new BlockerDeclarationDto(Card(b.BlockerInstanceId), Card(b.AttackerInstanceId)))
                    .ToList(),
            },
            // OrderTriggersCommand carries stack-object ids (not card ids) that
            // are freshly minted per run; an empty list ("as presented") is the
            // determinism-safe replay choice and is what the scripted driver
            // emits. Pass through; the engine tolerates unknown ids.
            _ => withSeat,
        };
    }

    /// <summary>
    /// Resolve a live card by name on the rebuilt facade, biased to the seat
    /// that owned the original id when known. Returns the live InstanceId, or
    /// null if no live card of that name exists. Used by replay rebinding —
    /// best-effort, since duplicate-named cards (a legendary pair) are
    /// interchangeable for the structural bar.
    /// </summary>
    private Guid? ResolveLiveCardByName(
        string name, Guid originalId, IReadOnlyDictionary<Guid, string> instanceNames)
    {
        foreach (var player in new[] { _alice, _bob })
        {
            foreach (var zoneType in new[]
                     {
                         ZoneType.Battlefield, ZoneType.Hand, ZoneType.Graveyard,
                         ZoneType.Library, ZoneType.Exile, ZoneType.Stack,
                     })
            {
                foreach (var card in player.Zones.GetZone(zoneType).GetCards())
                {
                    if (string.Equals(card.Name, name, StringComparison.Ordinal))
                    {
                        return card.InstanceId;
                    }
                }
            }
        }
        return null;
    }

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
            BottomCount: payload?.BottomCount,
            ChoiceView: payload?.ChoiceView,
            DamageDivisionView: payload?.DamageDivisionView,
            PlayerCandidates: payload?.PlayerCandidates,
            StackCandidates: payload?.StackCandidates,
            ScryView: payload?.ScryView);
    }

    private void BridgeEvent(GameEvent e)
    {
        var type = e.GetType().Name;
        // Snapshot the currently-tracked turn state for the payload builder.
        // Phase labels are already first-class (PreCombatMain / PostCombatMain)
        // since Slice 3; turn state is carried for PhaseStateChangedEvent and
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

        // Wire timestamp: stamp wall-clock UTC at the EventDto build seam, NOT
        // e.Timestamp. Since the LogicalClock change e.Timestamp is a logical
        // epoch (2000-01-01 + tick offset) used only for deterministic ordering
        // inside the engine; surfacing it on the wire made the portal render
        // "Jan 1 2000". Seq remains the load-bearing ordering field; At is the
        // human-facing wall-clock display value. Read once per engine event so
        // every per-viewer variant of this event shares the same At.
        var wireAt = DateTime.UtcNow;

        var publicDto = new EventDto(
            EventId: e.EventId,
            Type: type,
            At: wireAt,
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
                    At: wireAt,
                    Payload: EventPayloadBuilder.Build(e, _alice, turnState),
                    Seq: seq),
                [_bob.Id] = new EventDto(
                    EventId: e.EventId,
                    Type: type,
                    At: wireAt,
                    Payload: EventPayloadBuilder.Build(e, _bob, turnState),
                    Seq: seq),
            };
        }

        var envelope = new EventEnvelope(publicDto, perPlayer);

        // Replay suppression (PLAN 08 / Phase 29.x): the state fold + seq stamp
        // above ALWAYS run (so the rebuilt facade's GetState catches up to the
        // captured state), but during a FromSnapshot fast-forward we must NOT
        // re-broadcast the historical events to any subscriber — they already
        // happened in the original game. Skip the external fan-out only.
        if (_suppressEventFanout)
        {
            return;
        }

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
