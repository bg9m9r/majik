using System.Text.Json;
using Majik.Core.Abilities;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Api;

/// <summary>
/// Per-game aggregate exposed to the outside world. Wraps a live engine
/// (event bus, stack, players, trigger manager, priority loop) with a
/// command/state/events interface that's safe to ship over HTTP/JSON.
///
/// Phase 9 scope: 1v1, both players are <see cref="RemoteAgent"/>s, single
/// priority round driver. Phase 10 adds full turn/phase looping.
/// </summary>
public sealed class GameFacade
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly PriorityManager _priority;
    private readonly Player _alice;
    private readonly Player _bob;
    private readonly RemoteAgent _aliceAgent;
    private readonly RemoteAgent _bobAgent;
    private readonly PriorityLoop _loop;
    private Task? _loopTask;
    private readonly List<Action<EventDto>> _subscribers = new();
    private readonly ActionLog _log = new();

    public Guid GameId { get; } = Guid.NewGuid();

    public bool IsRoundComplete => _loopTask?.IsCompleted ?? false;

    private GameFacade(Player alice, Player bob)
    {
        _alice = alice;
        _bob = bob;

        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _zones = new ZoneService(_bus);
        _resolver = new StackResolver(_bus, _zones);
        _priority = new PriorityManager(new List<Player> { alice, bob }, _stack, _bus, _triggers);

        _aliceAgent = new RemoteAgent(alice, LookupCard);
        _bobAgent = new RemoteAgent(bob, LookupCard);

        _loop = new PriorityLoop(
            players: new[] { alice, bob },
            priority: _priority,
            stack: _stack,
            stackResolver: _resolver,
            zoneService: _zones,
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [alice] = _aliceAgent,
                [bob] = _bobAgent,
            },
            turnNumberAccessor: () => 1,
            phaseAccessor: () => PhaseStateType.Main);

        _bus.SubscribeAll(BridgeEvent);
    }

    public static GameFacade Create(string aliceName, string bobName)
        => new(new Player(aliceName, 20), new Player(bobName, 20));

    /// <summary>
    /// Kicks off the priority round. Returns immediately; the loop awaits on
    /// the first player's agent. Submit commands via <see cref="SubmitAsync"/>
    /// to advance.
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (_loopTask != null)
        {
            throw new InvalidOperationException("Game already started.");
        }

        _loopTask = _loop.RunUntilRoundEndsAsync(_alice, ct);

        // Yield once so the loop hits its first await.
        return Task.Delay(1, ct);
    }

    /// <summary>
    /// Submit a player command. Returns when the engine has processed it
    /// (which may include resolving stack objects and reaching the next
    /// prompt).
    /// </summary>
    public Task SubmitAsync(GameCommand command, CancellationToken ct = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        var agent = command.PlayerId == _alice.Id ? _aliceAgent
                  : command.PlayerId == _bob.Id ? _bobAgent
                  : throw new InvalidOperationException($"Unknown player {command.PlayerId}.");

        agent.Submit(command);
        _log.Append(command);

        // Give the loop a tick to consume the result.
        return Task.Delay(1, ct);
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
        GameId, turnNumber: 1, phase: PhaseStateType.Main,
        activePlayer: _priority.CurrentPlayer ?? _alice,
        players: new[] { _alice, _bob },
        stack: _stack);

    /// <summary>Per-player snapshot. CR 706 hidden information (opponent
    /// hand) is masked. Pass the requesting player's id; returns null
    /// when the id matches no slot in this game.</summary>
    public GameStateDto? GetStateFor(Guid viewerPlayerId)
    {
        var viewer = ResolveSlot(viewerPlayerId);
        if (viewer == null) return null;

        return StateSnapshotter.Snapshot(
            GameId, turnNumber: 1, phase: PhaseStateType.Main,
            activePlayer: _priority.CurrentPlayer ?? _alice,
            players: new[] { _alice, _bob },
            stack: _stack,
            viewer: viewer);
    }

    private Player? ResolveSlot(Guid id)
    {
        if (_alice.Id == id) return _alice;
        if (_bob.Id == id) return _bob;
        return null;
    }

    /// <summary>
    /// Stream events. Returned <see cref="IDisposable"/> unsubscribes.
    /// </summary>
    public IDisposable Subscribe(Action<EventDto> handler)
    {
        _subscribers.Add(handler);
        return new Subscription(() => _subscribers.Remove(handler));
    }

    /// <summary>
    /// Subscribe to prompt envelopes. Fires once each time the engine
    /// transitions to awaiting a command from either player. Returned
    /// <see cref="IDisposable"/> detaches the handler.
    /// </summary>
    public IDisposable SubscribePrompts(Action<PromptDto> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Action<IReadOnlyList<Type>> aliceHandler = kinds => handler(BuildPrompt(_alice, kinds));
        Action<IReadOnlyList<Type>> bobHandler = kinds => handler(BuildPrompt(_bob, kinds));
        _aliceAgent.PromptRequested += aliceHandler;
        _bobAgent.PromptRequested += bobHandler;
        return new Subscription(() =>
        {
            _aliceAgent.PromptRequested -= aliceHandler;
            _bobAgent.PromptRequested -= bobHandler;
        });
    }

    private PromptDto BuildPrompt(Player player, IReadOnlyList<Type> kinds)
        => new(GameId, player.Id, kinds.Select(t => t.Name).ToList());

    private void BridgeEvent(GameEvent e)
    {
        var dto = new EventDto(
            EventId: e.EventId,
            Type: e.GetType().Name,
            At: e.Timestamp,
            Payload: EventPayloadBuilder.Build(e));

        foreach (var sub in _subscribers.ToList())
        {
            sub(dto);
        }
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

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;
        public Subscription(Action dispose) { _dispose = dispose; }
        public void Dispose() => _dispose();
    }
}
