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

    // CR 505 — the phase value itself distinguishes PreCombatMain from
    // PostCombatMain (Slice 3), so phase/step labels are unambiguous. We
    // still track the outer turn-level state (CR 500.1 turn structure) here
    // and publish a TurnStateChangedEvent at each turn-state boundary for
    // TurnStateChangedEvent consumers (GameFacade._currentTurnState).
    // Starts null; the first SetTurnState at turn start populates it.
    private TurnStateType? _currentTurnState;

    /// <summary>
    /// Per-turn event tally — creatures died, permanents left, cards drawn.
    /// Reset at the start of each turn; consulted by revolt / connive / draw-watchers.
    /// </summary>
    public TurnState TurnState { get; } = new();

    /// <summary>
    /// The game-level day/night designation (CR 730, "Day and Night").
    /// Starts at <see cref="DayNightDesignation.Neither"/>; the untap-step
    /// check (CR 502.2 / 730.2) is applied at the start of each turn from
    /// the previous turn's active player's spell count. Daybound/nightbound
    /// permanents (CR 702.145) and "becomes day/night" effects drive it day
    /// or night.
    /// </summary>
    public DayNightState DayNight { get; } = new();

    // CR 502.2 / 730.2 — the untap-step day/night check inspects the
    // PREVIOUS turn's active player and how many spells they cast THAT turn.
    // TurnState.Reset() (run at the top of each RunTurnAsync) wipes the
    // per-turn spell tally, so we snapshot the just-ended turn's active
    // player + their cast count here before the reset and feed it into the
    // check during this turn's untap step. Null before the first turn ends.
    private Player? _previousTurnActivePlayer;
    private int _previousTurnActivePlayerSpellsCast;

    /// <summary>Effects that grant the current turn an additional combat
    /// phase (Aggravated Assault, Combat Celebrant, Relentless Assault)
    /// enqueue here. The turn loop re-runs the combat sequence as long
    /// as the queue is non-empty.</summary>
    public AdditionalCombatQueue AdditionalCombats => _additionalCombats;

    private readonly Majik.Core.Events.IEventBus? _eventBus;
    private readonly Func<ICard, Player, Majik.Core.Stack.Stack?, Majik.Core.Game.SpellDefinition?>? _spellDefResolver;
    // Slice 5a — server-side auto-pass plumbing forwarded into every
    // per-round PriorityLoop. Null = pre-Slice-5a behaviour (always
    // prompt). Wired by GameFacade.StartFullGameAsync.
    private readonly Func<Player, IAutoPassPrefsView?>? _autoPassPrefsProvider;
    private readonly Func<GameContext, bool>? _isPassOnlyDeadWindow;
    private readonly Func<DateTime>? _clock;

    // CR 720 — registry of "control another player" grants (Mindslaver,
    // Emrakul, the Promised End). At turn-start the driver consumes any
    // grant pending for the active player so every agent lookup for that
    // player (priority, targets, combat, mana) routes to the controller's
    // agent for the duration of the turn; at turn-end the grant is cleared.
    // Null in legacy / unit harnesses that don't wire control — control is
    // then simply never active.
    private readonly Majik.Core.Players.ControlPlayerRegistry? _controlRegistry;

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
        Majik.Core.Effects.ReplacementBus? replacements = null,
        Func<Player, IAutoPassPrefsView?>? autoPassPrefsProvider = null,
        Func<GameContext, bool>? isPassOnlyDeadWindow = null,
        Func<DateTime>? clock = null,
        Majik.Core.Players.ControlPlayerRegistry? controlRegistry = null)
    {
        _controlRegistry = controlRegistry;
        _autoPassPrefsProvider = autoPassPrefsProvider;
        _isPassOnlyDeadWindow = isPassOnlyDeadWindow;
        _clock = clock;
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
        _eventBus?.Subscribe<CardCycledEvent>(OnCardCycled);
    }

    // -----------------------------------------------------------------
    // TurnState event handlers
    // -----------------------------------------------------------------

    private void OnCardMoved(CardMovedEvent e)
    {
        // CR 701.16a — Hand → Graveyard moves are discards. Track the
        // discarder (= moved card's owner) for "for each card you've
        // discarded this turn" reducers (Hollow One). Counts every
        // hand → graveyard move regardless of source — player discard,
        // opponent-forced discard, "discard a card" cost payments all
        // qualify per CR 701.16a. Spells leaving the hand to the stack
        // do NOT match this branch (they move Hand → Stack), so casting
        // Hollow One itself does not pre-bump its own reducer.
        if (e.FromZone == ZoneType.Hand && e.ToZone == ZoneType.Graveyard)
        {
            TurnState.RecordCardDiscarded(e.Card.Owner);
        }

        // Track lands entering under a player's control this turn (CR 702.142
        // landfall + landfall-conditional spells like Searing Blaze). This
        // fires off the same CardMovedEvent funnel as the leavers below; the
        // entering branch must run BEFORE the early-return for non-leavers.
        if (e.ToZone == ZoneType.Battlefield && e.Card.HasType(CardType.Land))
        {
            TurnState.RecordLandEnteredBattlefield(e.Card.Controller);
        }

        // Per-permanent ETB-this-turn ledger (CR 700.6 — read by
        // Force of Despair's "creatures that entered the battlefield this
        // turn" filter at resolution).
        if (e.ToZone == ZoneType.Battlefield && e.Card is Majik.Core.Cards.Permanent permanent)
        {
            TurnState.RecordPermanentEnteredBattlefield(permanent);
        }

        // Only track permanents leaving the battlefield (Rule 702.104).
        if (e.FromZone != ZoneType.Battlefield) return;

        var formerController = e.Card.Controller;

        TurnState.RecordPermanentLeftBattlefield(formerController);

        // Per-card "moved to graveyard from battlefield this turn" ledger
        // (CR 121 — read by Faith's Reward at resolution). Only the
        // Battlefield → Graveyard transition qualifies (Faith's Reward's
        // printed wording is precise; exile / hand / library don't count).
        if (e.ToZone == ZoneType.Graveyard)
        {
            TurnState.RecordPermanentMovedToGraveyard(formerController, e.Card);
        }

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

    private void OnCardCycled(CardCycledEvent e)
    {
        // CR 702.32 — record a cycle for the cycling player. Read by Hollow
        // One's self-cost-reduction reducer ("for each card you've cycled
        // or discarded this turn"). Note: the cycled card also moves
        // Hand → Graveyard which OnCardMoved counts as a discard — Hollow
        // One's reducer reads cycles + discards as DISTINCT counters per
        // the printed oracle text ("cycled OR discarded"), so the same
        // act of cycling contributes to BOTH tallies. Real card matches
        // (see Hollow One rulings — cycling counts as both a cycle and a
        // discard for cards that reference either).
        TurnState.RecordCardCycled(e.Player);
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

        // CR 720.1 — if another player was granted control of this turn's
        // player (Mindslaver / Emrakul), promote that pending grant to
        // active control now, before any decision is solicited. While
        // active, every `_agents[activePlayer]` lookup (the ControlAware
        // agent map wired by GameDriver) routes to the controller's agent,
        // so the controller makes all of this player's plays (CR 720.2/720.3
        // — only decisions move; the active player's cards, hand, life, and
        // library stay theirs and their permanents still untap below).
        // Consumed here = a grant lasts exactly one turn (CR 720.1 — "that
        // player's next turn").
        _controlRegistry?.ConsumeControlFor(activePlayer, out _);

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

        // CR 502.2 / 730.2 — snapshot the JUST-ENDED turn's active player and
        // their spell count BEFORE TurnState.Reset() wipes the tally. The
        // untap-step day/night check (below) reads this snapshot; it inspects
        // the previous turn's active player, not the player whose turn is now
        // beginning.
        _previousTurnActivePlayerSpellsCast = _previousTurnActivePlayer != null
            ? TurnState.SpellsCastByPlayer(_previousTurnActivePlayer)
            : 0;

        // Reset per-turn event tally (revolt, connive X, draw watchers).
        TurnState.Reset();

        // CR 500.1 — fresh turn restarts the turn-state sequence. Clear the
        // tracked state so the TurnBeginning transition below always fires,
        // even though the previous turn ended in TurnEnding.
        _currentTurnState = null;

        // Beginning phase (CR 501-504: Untap, Upkeep, Draw).
        SetTurnState(TurnStateType.TurnBeginning);
        SetPhase(PhaseStateType.Untap);

        // CR 502.2 / 730.2 — the second turn-based action of the untap step:
        // the day/night check. If it's day and the previous turn's active
        // player cast no spells, it becomes night; if it's night and they
        // cast two or more, it becomes day; if it's neither, no check. Runs
        // before untap proper (order among untap turn-based actions is
        // immaterial — none interact). On the very first turn there is no
        // previous active player, so the snapshot is 0 and the game (still
        // "neither" until a daybound permanent/effect cares) is unaffected.
        CheckDayNightUntapTransition();

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

        // Main 1 (CR 505 — precombat main phase).
        SetTurnState(TurnStateType.PreCombatMain);
        SetPhase(PhaseStateType.PreCombatMain);
        // CR 714.2 — Saga lore-counter tick fires at the precombat main.
        AdvanceSagas(activePlayer);
        await PriorityRound(activePlayer, ct);

        // Combat (CR 506-511).
        SetTurnState(TurnStateType.Combat);
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

        // CR 511 — end of combat step. Emit the step event so "until end of
        // combat" durations (e.g. Firebending mana) can expire before the
        // postcombat main begins.
        SetPhase(PhaseStateType.EndOfCombat);

        // Main 2 (CR 505 — postcombat main phase).
        SetTurnState(TurnStateType.PostCombatMain);
        SetPhase(PhaseStateType.PostCombatMain);
        await PriorityRound(activePlayer, ct);

        // End phase (CR 512-514: End step, Cleanup).
        SetTurnState(TurnStateType.TurnEnding);
        SetPhase(PhaseStateType.End);
        await PriorityRound(activePlayer, ct);

        SetPhase(PhaseStateType.Cleanup);
        Cleanup(activePlayer);

        // CR 502.2 / 730.2 — remember this turn's active player so the NEXT
        // turn's untap-step day/night check can read how many spells they
        // cast. Recorded after the turn's body so their full spell count is
        // captured (the snapshot read happens at the top of the next
        // RunTurnAsync, before TurnState.Reset()).
        _previousTurnActivePlayer = activePlayer;

        // CR 720.1 — control lasts only for "that player's next turn". Now
        // that the turn is over, drop the active control so the following
        // turn's player makes their own decisions again. No-op when no
        // control was active this turn.
        _controlRegistry?.ClearActiveControl();
    }

    /// <summary>
    /// CR 502.2 / CR 730.2 — the untap-step day/night check. If it's day and
    /// the previous turn's active player cast no spells, it becomes night; if
    /// it's night and they cast two or more, it becomes day; if it's neither,
    /// no check happens (CR 730.2c). Publishes a
    /// <see cref="Majik.Core.Events.DayNightChangedEvent"/> when the
    /// designation actually changes so daybound/nightbound transform logic
    /// (CR 702.145) and clients can react.
    /// </summary>
    private void CheckDayNightUntapTransition()
    {
        // CR 730.2 — the check inspects the PREVIOUS turn. On the very first
        // turn of the game there is no previous turn, so the check doesn't
        // happen (it can't flip day→night off a phantom zero-spell turn).
        if (_previousTurnActivePlayer == null) return;

        var changed = DayNight.CheckUntapTransition(_previousTurnActivePlayerSpellsCast);
        if (changed)
        {
            // CR 702.145c / 702.145f — transform daybound/nightbound
            // permanents to reflect the new designation BEFORE announcing
            // the change, so DayNightChangedEvent subscribers observe the
            // already-transformed faces.
            ApplyDayboundNightboundTransforms();
            _eventBus?.Publish(new Majik.Core.Events.DayNightChangedEvent(DayNight.Designation));
        }
    }

    /// <summary>
    /// CR 702.145c / 702.145f — flip every daybound/nightbound permanent on
    /// every player's battlefield to match the current day/night designation
    /// (front-face daybound → back when night; back-face nightbound → front
    /// when day). Driven by the live <see cref="DayNightState"/>.
    /// </summary>
    private void ApplyDayboundNightboundTransforms()
    {
        foreach (var p in _players)
        {
            var permanents = p.Zones.Battlefield.GetCards().OfType<Card>().ToList();
            Majik.Core.Keywords.DayboundNightbound.OnDayNightChanged(permanents, DayNight.Designation);
        }
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

    /// <summary>
    /// CR 500.1 — advance the outer turn-level state and publish a
    /// <see cref="Majik.Core.Events.TurnStateChangedEvent"/> so downstream
    /// wire code can recover which main phase we're in (CR 505). This is
    /// the only place the live turn flow surfaces the turn-state; the
    /// dedicated <see cref="StateMachine.TurnStateMachine"/> is not driven
    /// in the production match path. No-op when the state hasn't actually
    /// changed, so repeated entries (e.g. extra combat phases re-entering
    /// Combat) don't emit redundant events.
    /// </summary>
    private void SetTurnState(TurnStateType turnState)
    {
        if (_currentTurnState == turnState) return;
        var previous = _currentTurnState;
        _currentTurnState = turnState;
        _eventBus?.Publish(new Majik.Core.Events.TurnStateChangedEvent(previous, turnState));
    }

    private void UntapStep(Player active)
    {
        var permanents = active.Zones.Battlefield.GetCards().OfType<Permanent>().ToList();

        // CR 502.1 — first pass: collect tapped permanents not already
        // gated by ShouldSkipUntap (Mana Vault self-skip, Choke symmetric
        // subtype filter, Stasis-style global skip). These are the
        // "candidates" the count caps then thin further.
        var candidates = new List<Permanent>();
        foreach (var card in permanents)
        {
            if (!card.IsTapped) continue;
            if (Majik.Core.Effects.UntapStepRestrictions.ShouldSkipUntap(card, active)) continue;
            candidates.Add(card);
        }

        // CR 502.1 — second pass: apply count caps (Static Orb / Winter
        // Orb / Smoke "can't untap more than N <filter>"). Returns the
        // set of permanents blocked by at least one active cap; remaining
        // candidates untap normally. Empty set when no caps registered.
        var blockedByCap = Majik.Core.Effects.UntapStepRestrictions
            .ApplyCountCaps(candidates, active);

        foreach (var card in permanents)
        {
            if (card.IsTapped
                && !Majik.Core.Effects.UntapStepRestrictions.ShouldSkipUntap(card, active)
                && !blockedByCap.Contains(card))
            {
                // CR 122.1g — stun counters replace untapping. If a permanent
                // with a stun counter on it would become untapped, instead
                // remove a stun counter from it (it stays tapped). One untap
                // consumes exactly one stun counter; a permanent with multiple
                // stun counters needs that many untap steps to clear them. The
                // counter mutation is the observable surface (the permanent's
                // CounterCollection); no untap event fires.
                if (card.Counters.Count(Majik.Core.Counters.CounterType.Stun) > 0)
                {
                    card.Counters.Remove(Majik.Core.Counters.CounterType.Stun, 1);
                }
                else
                {
                    card.Untap();
                }
            }
            // CR 502 — clears summoning sickness, loyalty-once-per-turn,
            // and any other turn-scoped per-permanent flags. Always runs,
            // even for permanents whose untap was gated by a skip or cap.
            card.ResetTurnState();
        }
    }

    private void AdvanceSagas(Player active)
    {
        // CR 714.2 / 714.2b — at the precombat main, each Saga its controller
        // controls adds a lore counter and triggers the matching chapter
        // ability. When the Saga was bound with a live TriggerManager (the
        // production path), AdvanceAndChapter ENQUEUES the chapter ability as a
        // triggered ability rather than resolving it in-line; the PreCombatMain
        // PriorityRound that runs immediately after this call drains it onto the
        // stack (CR 603.3), so an opponent gets a priority window to respond
        // before the chapter resolves (e.g. before a transforming Saga's
        // chapter III flips). Sagas bound without a TriggerManager fall back to
        // synchronous chapter resolution.
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

            // CR 712.3 / 712.4 — Modal Double-Faced Card: real cast-either-face
            // (deferral #3). When the card in hand carries a castable back-face
            // descriptor, prompt the controller to choose which face to cast.
            //   * Front face → fall through and cast the front card normally.
            //   * Back LAND face (Soporific Springs) → play it as a land with
            //     no stack and return (no spell cast / mana payment).
            //   * Back SPELL face → swap the cast card + definition + cost to
            //     the back face and cast it onto the stack.
            // No transform machinery — only the chosen face exists.
            ICard castCard = cast.Card;
            var def = (Majik.Core.Game.SpellDefinition?)null;
            Majik.Core.ValueObjects.ManaCost? faceCostOverride = null;
            if (cast.AlternativeCost == null && castCard is Majik.Core.Cards.Card mdfcCard
                && mdfcCard.MdfcState is { CanCastEitherFace: true })
            {
                var chosenFace = await MdfcCastFlow.ResolveFaceAsync(
                    castCard, actor, _agents[actor], ctx, ct);
                if (chosenFace != null)
                {
                    if (chosenFace.IsLand)
                    {
                        // CR 305 / 712.3 — back land face: play, no stack.
                        MdfcCastFlow.PlayBackLandFace(
                            frontCard: castCard,
                            backFace: chosenFace,
                            caster: actor,
                            zones: _zoneService,
                            replacements: _replacements,
                            landDropTracker: _landDropTracker,
                            activePlayer: ctx.ActivePlayer,
                            phase: _currentPhase,
                            stackEmpty: _stack.IsEmpty,
                            effects: _continuousEffects);
                        return;
                    }

                    // CR 712.3 — back spell face: swap the cast object to a
                    // freshly-built back-face spell, with its own cost / def.
                    var backCard = chosenFace.BuildCard(actor, _replacements);
                    backCard.SetOwner(actor);
                    if (backCard is Majik.Core.Cards.Card backConcrete)
                    {
                        backConcrete.SetController(actor);
                    }
                    if (backCard is Majik.Core.Cards.Creature backCreature
                        && _continuousEffects != null)
                    {
                        backCreature.ActiveEffects = _continuousEffects;
                    }
                    // Replace the front card in hand with the back-face card so
                    // the Hand → Stack move in SpellCastFlow finds it there.
                    if (castCard.Zone == Majik.Core.Zones.ZoneType.Hand
                        && castCard.Owner != null)
                    {
                        castCard.Owner.Zones.Hand.RemoveCard(castCard);
                    }
                    backCard.SetZone(Majik.Core.Zones.ZoneType.Hand);
                    actor.Zones.Hand.AddCard(backCard);
                    castCard = backCard;
                    def = chosenFace.BuildDefinition(actor, raw => raw, _stack, _zoneService);
                    faceCostOverride = Majik.Core.ValueObjects.ManaCost.Parse(chosenFace.ManaCost);
                }
            }

            // Resolve a proper SpellDefinition via the injected resolver
            // (oracle-text → effects binder). Fall back to vanilla — fine
            // for permanents (StackResolver puts them on the battlefield);
            // for instants/sorceries with no binder match, casting would
            // waste the card, so we skip and rotate.
            var resolved = def ?? _spellDefResolver?.Invoke(castCard, actor, _stack);
            var isPermanent = castCard.HasType(Majik.Core.Cards.Types.CardType.Creature)
                || castCard.HasType(Majik.Core.Cards.Types.CardType.Artifact)
                || castCard.HasType(Majik.Core.Cards.Types.CardType.Enchantment)
                || castCard.HasType(Majik.Core.Cards.Types.CardType.Planeswalker);
            if (resolved == null && !isPermanent)
            {
                RotateHand(castCard, "no SpellDef for instant/sorcery");
                return;
            }
            def = resolved
                ?? Majik.Core.Game.SpellDefinition.Vanilla(_ => Array.Empty<Majik.Core.Abilities.IEffect>());

            // Pay mana up front. SpellCastFlow doesn't enforce payment;
            // it just collects ManaPayment for downstream metadata.
            // When the agent elected an alternative cost (CR 118.9 —
            // flashback / spectacle / evoke / pitch), it REPLACES the
            // printed cost and bypasses cost-reduction; otherwise apply
            // CR 117.7 Affinity / cost-reducers on the printed cost.
            // CR 117.7 / 601.2f — pass the live player roster so the
            // three-arg overload folds in SpellCostIncreaseAbility riders
            // from every player's battlefield (Sphere of Resistance,
            // Trinisphere, Thalia, Damping Sphere).
            var cost = faceCostOverride
                ?? cast.AlternativeCost?.AlternativeManaCost
                ?? Majik.Core.Costs.CostReduction.GetEffectiveCost(castCard, actor, _players);

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

                // Portal "Auto-pay": the mana-cost prompt's Auto-pay button
                // returns an empty (non-cancelled) source list meaning
                // "tap my untapped lands for me". When the floating pool
                // doesn't already cover the cost, ask the resolver to
                // greedily auto-select untapped sources. If it can't (hybrid/
                // Phyrexian pips, or not enough mana), fall through to the
                // existing Pay call — which fails gracefully and rotates the
                // hand, same as before.
                if (payment.Sources.Count == 0
                    && !actor.ManaPool.CanPay(cost)
                    && manaResolver.TryAutoSelectSources(actor, cost, out var autoPayment))
                {
                    payment = autoPayment;
                }
            }
            // CR 106.4 — pass the cast card as the "spent on" context so
            // slot-level mana provenance (Arena of Glory's exert haste rider,
            // deferral #1) can react to "if THAT mana is spent on THIS spell".
            if (!manaResolver.Pay(actor, cost, payment, spentOn: castCard, out _, out var colorCounts))
            {
                RotateHand(castCard, "Pay failed");
                return;
            }

            // CR 702.44b — stamp the per-color spent ledger on this cast so
            // ETB effects can read it off the resolving permanent (parallels
            // PendingCastX). SetPendingCastColorCounts also derives the
            // distinct-color set (PendingCastColors) for Sunburst, while the
            // count ledger preserves multiplicity so "{R}{R} was spent"
            // intervening-ifs (Vibrance / Wistfulness) can distinguish
            // {R}{R} from {R}{G}. The resolver computed the per-color counts
            // by diffing the pool across the spend (colored pips + colored
            // mana used to satisfy generic). Empty ledger = no colored mana
            // spent → Sunburst yields zero counters. Consumed + cleared by
            // the ETB effect.
            if (castCard is Majik.Core.Cards.Card concreteForColors)
            {
                concreteForColors.SetPendingCastColorCounts(colorCounts);
            }

            try
            {
                // Forward the already-prompted mana payment so SpellCastFlow
                // doesn't re-prompt (CR 601.2g — one mana selection per cast).
                await castFlow.CastAsync(
                    actor, castCard, def, _agents[actor], ctx, ct,
                    additionalCosts: cast.AdditionalCosts,
                    alternativeCost: cast.AlternativeCost,
                    preChosenMana: payment);
            }
            catch (InvalidOperationException ex)
            {
                RotateHand(castCard, $"CastAsync threw: {ex.Message}");
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
                    // Resolve any lazy CandidateGatherer against the live ctx
                    // (mirrors AbilityActivationFlow / SpellCastFlow / TriggerManager
                    // so the activated-ability dispatcher path honours the same
                    // gatherer surface).
                    var live = req.ResolveCandidates(ctx);
                    var promptReq = ReferenceEquals(live, req.LegalCandidates)
                        ? req
                        : req.WithCandidates(live);
                    var chosen = await _agents[actor].ChooseTargetsAsync(ctx, promptReq, ct: default);
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
            manaAbilityDispatcher: DispatchManaAbility,
            // Slice 5a — forward server-side auto-pass plumbing into
            // every priority round. All four are null in the legacy
            // bot-vs-bot harnesses (TurnDriver constructed without these
            // params) — PriorityLoop's auto-pass gate is then disabled.
            autoPassPrefsProvider: _autoPassPrefsProvider,
            isPassOnlyDeadWindow: _isPassOnlyDeadWindow,
            eventBus: _eventBus,
            clock: _clock);

        try
        {
            await loop.RunUntilRoundEndsAsync(activePlayer, ct);
        }
        finally
        {
            // Slice 5a — TurnDriver constructs a fresh PriorityLoop per
            // priority round; without detach the bus would accumulate
            // two handlers per round across the full game lifetime.
            loop.DetachFromBus();
        }
    }

    private async Task RunCombat(Player attacker, Player defender, CancellationToken ct)
    {
        var eligibleAttackers = attacker.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            // CR 508.1c / 302.6 — eligible iff untapped AND (no summoning
            // sickness OR has haste). Without the haste check, freshly
            // hasted creatures (Lightning Greaves, Hexdrinker, etc.) would
            // never be offered as attackers.
            .Where(c => !c.IsTapped
                && (!c.HasSummoningSickness || Majik.Core.Combat.CombatAbilities.HasHaste(c)))
            .ToList();
        var eligibleBlockers = defender.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !c.IsTapped)
            .ToList();

        // UX fast-path: if the active player has zero eligible attackers,
        // skip the DeclareAttackers prompt and the per-attacker plumbing
        // entirely. Full Control opts back into prompting (the human asked
        // for every window). Mirrors the "skip combat" affordance shipped
        // by MTG Arena / MTGO. Rules: no attackers declared → no combat
        // damage step would fire anyway (CR 508.2 — empty Attackers list
        // is legal). We do still publish StepStartedEvent for the phase
        // (SetPhase already fired at the caller) so "until end of combat"
        // and step-aware triggers continue to land.
        var fullControl = _autoPassPrefsProvider?.Invoke(attacker)?.FullControl ?? false;
        if (eligibleAttackers.Count == 0 && !fullControl)
        {
            // Still run a priority round on the empty combat step so the
            // defender can react to triggers / play instants (rare but
            // legal — e.g. opponent's Stoneforge Mystic triggers on combat).
            // The auto-pass gate will burn through this on default prefs.
            await PriorityRound(attacker, ct);
            return;
        }

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
