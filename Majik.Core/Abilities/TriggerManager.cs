using Majik.Core.Cards;
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
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly IEventBus? _eventBus;
    private readonly Action<GameEvent>? _globalHandler;

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

    /// <summary>
    /// Explicitly register an ability so it participates in evaluation. Idempotent.
    /// </summary>
    public void RegisterTriggeredAbility(ITriggeredAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        _abilities.Add(ability);
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

        _abilities.Add(ability);
    }

    public void UnregisterTriggeredAbility(ITriggeredAbility ability)
    {
        if (ability == null)
        {
            return;
        }

        _abilities.Remove(ability);
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

        foreach (var ability in _abilities.ToList())
        {
            if (!ability.IsTriggered(gameEvent))
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
                _abilities.Remove(ability);
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
        foreach (var ability in _abilities.ToList())
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
    /// across controllers, APNAP order is preserved.
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
                _stack.Push(ability);
            }
        }
    }

    public void Clear()
    {
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
                _abilities.Add(ability);
            }
            else
            {
                _abilities.Remove(ability);
            }
        }
    }
}
