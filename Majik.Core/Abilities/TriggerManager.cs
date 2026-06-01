using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Abilities;

/// <summary>
/// Service for managing triggered abilities (Rule 603).
///
/// Responsibilities:
///   - Track which triggered abilities are currently "active" (their source is in
///     one of the ability's ActiveZones).
///   - Listen to every game event and queue any ability whose condition matches.
///   - Drain the pending queue onto the stack in APNAP order the next time a
///     player would receive priority (Rule 603.3).
///
/// Two registration paths:
///   - <see cref="RegisterTriggeredAbility"/> / <see cref="UnregisterTriggeredAbility"/>:
///     explicit, for callers that manage lifecycle themselves (e.g. delayed triggers).
///   - <see cref="BindCard"/>: zone-driven; the manager subscribes to
///     <see cref="CardMovedEvent"/> and registers/unregisters the card's triggered
///     abilities as it moves into/out of their ActiveZones (Rule 603.6a).
/// </summary>
public class TriggerManager
{
    private readonly HashSet<ITriggeredAbility> _abilities = new();
    private readonly List<ITriggeredAbility> _pending = new();
    private readonly HashSet<ICard> _boundCards = new();

    // Cached snapshot of _abilities for the evaluation loops. Rebuilt lazily
    // when _abilitiesDirty is set by any mutator of _abilities. The loops
    // capture this array BEFORE iterating, so a registration/unregistration
    // performed mid-loop marks the snapshot dirty for the NEXT event but does
    // not affect the in-flight loop — exactly reproducing the old
    // `_abilities.ToList()` snapshot-per-call semantics with zero allocation
    // on the steady-state hot path (snapshot reused while membership is
    // stable).
    private ITriggeredAbility[] _abilitiesSnapshot = Array.Empty<ITriggeredAbility>();
    private bool _abilitiesDirty = true;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly IEventBus? _eventBus;
    private readonly Action<GameEvent>? _globalHandler;

    /// <summary>
    /// Number of active Torpor Orb-like effects suppressing creature ETB triggers.
    /// CR 603.3 — when &gt; 0, triggered abilities whose trigger event is a
    /// creature entering the battlefield are not added to the pending queue.
    /// Increment when such a permanent enters the battlefield;
    /// decrement when it leaves. See <see cref="Effects.TorporOrbStaticEffect"/>.
    /// </summary>
    public int CreatureEtbTriggerSuppressionCount { get; set; }

    public TriggerManager(Majik.Core.Stack.Stack stack, IEventBus? eventBus = null)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _eventBus = eventBus;

        if (_eventBus != null)
        {
            _globalHandler = OnAnyEvent;
            _eventBus.SubscribeAll(_globalHandler);
        }
    }

    public int PendingCount => _pending.Count;

    public bool IsRegistered(ITriggeredAbility ability) => _abilities.Contains(ability);

    /// <summary>Add to the registered set, marking the snapshot dirty if membership changed.</summary>
    private void AddAbility(ITriggeredAbility ability)
    {
        if (_abilities.Add(ability)) _abilitiesDirty = true;
    }

    /// <summary>Remove from the registered set, marking the snapshot dirty if membership changed.</summary>
    private bool RemoveAbility(ITriggeredAbility ability)
    {
        if (_abilities.Remove(ability))
        {
            _abilitiesDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>Rebuild the cached snapshot if a mutator marked it dirty since the last capture.</summary>
    private ITriggeredAbility[] AbilitiesSnapshot()
    {
        if (_abilitiesDirty)
        {
            _abilitiesSnapshot = _abilities.Count == 0
                ? Array.Empty<ITriggeredAbility>()
                : _abilities.ToArray();
            _abilitiesDirty = false;
        }
        return _abilitiesSnapshot;
    }

    /// <summary>
    /// Explicitly register an ability so it participates in evaluation. Idempotent.
    /// </summary>
    public void RegisterTriggeredAbility(ITriggeredAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        AddAbility(ability);
    }

    /// <summary>
    /// Register a delayed triggered ability (Rule 603.7). Identical to
    /// <see cref="RegisterTriggeredAbility"/> but the manager will
    /// auto-unregister the ability after it fires.
    /// </summary>
    public void RegisterDelayed(DelayedTriggeredAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        AddAbility(ability);
    }

    /// <summary>
    /// Place an already-triggered ability directly onto the pending queue
    /// (CR 603.3) without matching it against a game event. Used by engine
    /// surfaces that know an ability has triggered for game-state reasons the
    /// event bus does not model — notably a Saga chapter ability, which the
    /// engine fires when the lore counter reaches the chapter number
    /// (CR 714.2b). The ability is drained onto the stack on the next
    /// <see cref="PutPendingTriggersOnStack"/> / <c>...Async</c> call, exactly
    /// like an event-matched trigger, so an opponent receives a priority
    /// window before it resolves.
    /// </summary>
    public void EnqueuePending(ITriggeredAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        _pending.Add(ability);
    }

    public void UnregisterTriggeredAbility(ITriggeredAbility ability)
    {
        if (ability == null)
        {
            return;
        }

        RemoveAbility(ability);
        _pending.RemoveAll(t => ReferenceEquals(t, ability));
    }

    /// <summary>
    /// Track a card so its triggered abilities are auto-registered when it sits
    /// in any of their ActiveZones and unregistered when it leaves all of them.
    /// </summary>
    public void BindCard(ICard card)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        _boundCards.Add(card);
        SyncCardRegistration(card);
    }

    public void UnbindCard(ICard card)
    {
        if (card == null)
        {
            return;
        }

        _boundCards.Remove(card);
        foreach (var ability in card.Abilities.OfType<ITriggeredAbility>())
        {
            UnregisterTriggeredAbility(ability);
        }
    }

    /// <summary>
    /// Evaluate triggers for a game event. Matching abilities are added to the
    /// pending queue; nothing is pushed onto the stack here (Rule 603.3).
    /// </summary>
    public void EvaluateTriggers(GameEvent gameEvent)
    {
        if (gameEvent == null)
        {
            return;
        }

        // Capture the snapshot BEFORE the loop. A mid-loop register/unregister
        // marks the snapshot dirty for the next event but does not affect this
        // iteration (matches the old `_abilities.ToList()` per-call copy).
        var snapshot = AbilitiesSnapshot();
        foreach (var ability in snapshot)
        {
            if (!ability.IsTriggered(gameEvent))
            {
                continue;
            }

            // CR 614 / Torpor Orb — while CreatureEtbTriggerSuppressionCount > 0,
            // triggered abilities whose trigger event is a creature entering the
            // battlefield are suppressed (Rule 603.3).
            if (CreatureEtbTriggerSuppressionCount > 0
                && gameEvent is CardMovedEvent moved
                && moved.ToZone == ZoneType.Battlefield
                && moved.Card.HasType(CardType.Creature))
            {
                continue;
            }

            if (!ability.CanBePutOnStack())
            {
                continue;
            }

            _pending.Add(ability);

            if (ability is DelayedTriggeredAbility)
            {
                RemoveAbility(ability);
            }

            _eventBus?.Publish(new TriggeredAbilityTriggeredEvent(ability, gameEvent));
        }
    }

    /// <summary>
    /// Drain pending triggers onto the stack in APNAP order (Rule 603.3b).
    /// Push order ensures the last pushed ends up on top, so within a player
    /// the earliest-fired trigger resolves first.
    /// </summary>
    public void PutPendingTriggersOnStack(Player activePlayer)
    {
        if (activePlayer == null)
        {
            throw new ArgumentNullException(nameof(activePlayer));
        }

        if (_pending.Count == 0)
        {
            return;
        }

        var ordered = ApnapOrdering.Order(_pending, activePlayer);
        _pending.Clear();

        foreach (var ability in ordered)
        {
            _stack.Push(ability);
        }
    }

    /// <summary>
    /// Evaluate state-change trigger conditions (Rule 603.2c). Called by
    /// <see cref="Majik.Core.Rules.StateBasedActions"/> after each SBA pass.
    /// Fires on the rising edge of each <see cref="StateChangeTriggerCondition"/>.
    /// </summary>
    public void EvaluateStateChangeTriggers()
    {
        var snapshot = AbilitiesSnapshot();
        foreach (var ability in snapshot)
        {
            if (ability.Condition is not StateChangeTriggerCondition sc)
            {
                continue;
            }

            if (!sc.IsSatisfied())
            {
                continue;
            }

            if (!ability.CanBePutOnStack())
            {
                continue;
            }

            _pending.Add(ability);
        }
    }

    /// <summary>
    /// Async variant of <see cref="PutPendingTriggersOnStack"/>. Within each
    /// controller's group, the player's agent decides order (Rule 603.3b);
    /// across controllers, APNAP order is preserved. For triggers that declare
    /// <see cref="TriggeredAbility.TargetRequests"/>, the controller's agent is
    /// prompted for targets before the ability is pushed (Rule 603.3).
    /// </summary>
    public async Task PutPendingTriggersOnStackAsync(
        Player activePlayer,
        IReadOnlyDictionary<Player, IPlayerAgent> agents,
        GameContext ctx,
        CancellationToken ct = default)
    {
        if (activePlayer == null) throw new ArgumentNullException(nameof(activePlayer));
        if (agents == null) throw new ArgumentNullException(nameof(agents));

        if (_pending.Count == 0)
        {
            return;
        }

        var snapshot = _pending.ToList();
        _pending.Clear();

        // Group by controller, preserve APNAP across groups.
        var byController = snapshot
            .GroupBy(t => t.Controller)
            .OrderBy(g => ReferenceEquals(g.Key, activePlayer) ? 0 : 1)
            .ToList();

        foreach (var group in byController)
        {
            var mine = group.ToList();
            if (!agents.TryGetValue(group.Key, out var agent))
            {
                // No agent registered for this controller — fall back to timestamp.
                mine = mine.OrderBy(t => t.Timestamp).ToList();
            }
            else
            {
                var ordered = await agent.OrderTriggersAsync(ctx, mine, ct);
                mine = ordered.ToList();
            }

            foreach (var ability in mine)
            {
                // Prompt for targets if the concrete ability has declared any
                // TargetRequests (Rule 603.3). Abilities without requests skip
                // straight to the push.
                if (ability is TriggeredAbility ta && ta.TargetRequests.Count > 0)
                {
                    // PLAN 01 (Slice E) — shared targeting pipeline (CR 603.3).
                    // Triggers that compute candidates at resolution time (e.g.
                    // opponent's creatures, your graveyard) get a fresh pool
                    // every fire via each request's CandidateGatherer. A null
                    // agent (no agent registered for this controller) resolves
                    // every request to an empty pick — behaviour-preserving.
                    var collected = await Targeting.TargetCollection.CollectAsync(
                        ta.TargetRequests,
                        card: ta.Source as Cards.ICard,
                        ctx,
                        agent,
                        throwOnInsufficient: false,
                        ct);
                    ta.SetChosenTargets(collected);
                }

                _stack.Push(ability);
            }
        }
    }

    public void Clear()
    {
        if (_abilities.Count > 0) _abilitiesDirty = true;
        _abilities.Clear();
        _pending.Clear();
        _boundCards.Clear();
    }

    private void OnAnyEvent(GameEvent e)
    {
        // Self-published trigger events would cause infinite recursion.
        if (e is TriggeredAbilityTriggeredEvent)
        {
            return;
        }

        if (e is CardMovedEvent moved)
        {
            // Auto-bind any card with triggered abilities the first time we
            // see it cross a zone boundary. Without this, cards built by
            // ScryfallCardFactory never get their oracle-bound triggers
            // (Ragavan combat-damage Treasure, ETB life gain, etc.) wired
            // up because nobody calls BindCard explicitly.
            if (!_boundCards.Contains(moved.Card)
                && moved.Card.Abilities.OfType<ITriggeredAbility>().Any())
            {
                _boundCards.Add(moved.Card);
            }
            if (_boundCards.Contains(moved.Card))
            {
                SyncCardRegistration(moved.Card);
            }
        }

        EvaluateTriggers(e);
    }

    private void SyncCardRegistration(ICard card)
    {
        foreach (var ability in card.Abilities.OfType<ITriggeredAbility>())
        {
            if (ability.ActiveZones.Contains(card.Zone))
            {
                AddAbility(ability);
            }
            else
            {
                RemoveAbility(ability);
            }
        }
    }
}
