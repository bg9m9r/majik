using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
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
    private readonly LandDropTracker? _landDropTracker;
    private readonly AdditionalCombatQueue _additionalCombats = new();
    private PhaseStateType _currentPhase;
    private int _currentTurnNumber;

    /// <summary>Effects that grant the current turn an additional combat
    /// phase (Aggravated Assault, Combat Celebrant, Relentless Assault)
    /// enqueue here. The turn loop re-runs the combat sequence as long
    /// as the queue is non-empty.</summary>
    public AdditionalCombatQueue AdditionalCombats => _additionalCombats;

    private readonly Majik.Core.Events.IEventBus? _eventBus;

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
        Majik.Core.Events.IEventBus? eventBus = null)
    {
        _continuousEffects = continuousEffects;
        _landDropTracker = landDropTracker;
        _eventBus = eventBus;
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
        _stackResolver = stackResolver ?? throw new ArgumentNullException(nameof(stackResolver));
        _sba = stateBasedActions ?? throw new ArgumentNullException(nameof(stateBasedActions));
        _priorityManager = priorityManager ?? throw new ArgumentNullException(nameof(priorityManager));
        _combatFlow = combatFlow ?? throw new ArgumentNullException(nameof(combatFlow));
    }

    public async Task RunTurnAsync(Player activePlayer, int turnNumber, CancellationToken ct = default)
    {
        if (activePlayer == null) throw new ArgumentNullException(nameof(activePlayer));

        _currentTurnNumber = turnNumber;
        _eventBus?.Publish(new Majik.Core.Events.TurnStartedEvent(activePlayer, turnNumber));

        // CR 305.2 — land drops reset at turn start.
        _landDropTracker?.ResetTurn();

        // Beginning phase
        SetPhase(PhaseStateType.Untap);
        UntapStep(activePlayer);

        SetPhase(PhaseStateType.Upkeep);
        await PriorityRound(activePlayer, ct);

        SetPhase(PhaseStateType.Draw);
        if (turnNumber > 1)
        {
            DrawCard(activePlayer);
        }
        await PriorityRound(activePlayer, ct);

        // Main 1
        SetPhase(PhaseStateType.Main);
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

    private void SetPhase(PhaseStateType phase) => _currentPhase = phase;

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
        top.Zone = ZoneType.Hand;
    }

    private async Task PriorityRound(Player activePlayer, CancellationToken ct)
    {
        // Use the canonical bus if injected so SpellCastEvent reaches the
        // same subscribers as zone/stack/SBA events. Fallback: local bus
        // (events not externally visible — preserves prior behaviour).
        var castBus = _eventBus ?? new Majik.Core.Events.EventBus();
        var castFlow = new SpellCastFlow(_stack, _zoneService, castBus);
        var manaResolver = new Majik.Core.Costs.ManaPaymentResolver();

        async Task DispatchCast(Player actor, PriorityAction.CastSpell cast, GameContext ctx)
        {
            // MVP: vanilla spell definition — permanents resolve to battlefield,
            // instants/sorceries to graveyard (StackResolver handles both).
            // Future: wire ScryfallCardFactory.LookupSpellDefinition for
            // effect-bearing spells.
            var def = Majik.Core.Game.SpellDefinition.Vanilla(_ => Array.Empty<Majik.Core.Abilities.IEffect>());
            // Pay mana up front (the SpellCastFlow doesn't enforce payment;
            // it just collects ManaPayment for downstream effects).
            var cost = Majik.Core.ValueObjects.ManaCost.Parse(cast.Card.ManaCost);
            var payment = await _agents[actor].ChooseManaSourcesAsync(ctx, cost, ct);
            if (!manaResolver.Pay(actor, cost, payment))
            {
                // Couldn't pay — skip silently (bot mis-picked).
                return;
            }
            await castFlow.CastAsync(actor, cast.Card, def, _agents[actor], ctx, ct);
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
            castDispatcher: DispatchCast);

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
            discard.Zone = ZoneType.Graveyard;
            hand.RemoveAt(0);
        }

        // 2. Remove damage from creatures.
        foreach (var creature in _players.SelectMany(p => p.Zones.Battlefield.GetCards().OfType<Creature>()))
        {
            creature.ClearDamage();
        }

        // 3. Empty mana pools.
        foreach (var p in _players)
        {
            p.EmptyManaPool();
        }

        // 4. "Until end of turn" continuous effects expire (CR 514.2).
        _continuousEffects?.ExpireEndOfTurn();
    }
}
