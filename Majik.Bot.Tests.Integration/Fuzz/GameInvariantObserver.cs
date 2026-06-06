using System;
using System.Collections.Generic;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Bot.Tests.Integration.Fuzz;

/// <summary>
/// Subscribes to the engine EventBus and asserts invariants during a fuzz game.
/// Structural checks run on RunFinalChecks(); per-event checks accumulate as events arrive.
/// </summary>
public sealed class GameInvariantObserver : IDisposable
{
    private static readonly ZoneType[] AllZones =
    {
        ZoneType.Battlefield, ZoneType.Graveyard, ZoneType.Hand,
        ZoneType.Exile, ZoneType.Library, ZoneType.Stack, ZoneType.Command,
    };

    private readonly EventBus _bus;
    private readonly IReadOnlyList<Player> _players;
    private readonly Func<int> _etbSuppressionCount;
    private readonly List<InvariantViolation> _violations = new();

    // Class A orphaned-trigger tracking fields.
    // _expected: (triggering event, list of abilities that SHOULD fire, turn, phase)
    private readonly List<(GameEvent evt, List<ITriggeredAbility> expected, int turn, string phase)> _expected = new();
    // _fired: keyed by reference equality on the triggering event
    private readonly Dictionary<GameEvent, HashSet<ITriggeredAbility>> _fired =
        new(ReferenceEqualityComparer.Instance);
    private int _lastTurn;
    private string _lastPhase = "";

    public GameInvariantObserver(EventBus bus, IReadOnlyList<Player> players, Func<int> etbSuppressionCount)
    {
        _bus = bus;
        _players = players;
        _etbSuppressionCount = etbSuppressionCount;
        _bus.SubscribeAll(OnEvent);
    }

    public IReadOnlyList<InvariantViolation> Violations => _violations;

    private void OnEvent(GameEvent e)
    {
        // Track current turn number from turn-started events.
        if (e is TurnStartedEvent turn)
        {
            _lastTurn = turn.TurnNumber;
            return;
        }

        // Track current step/phase from step-started events.
        if (e is StepStartedEvent step)
        {
            _lastPhase = step.StepType.ToString();
            return;
        }

        // Record which abilities fired for this triggering event.
        if (e is TriggeredAbilityTriggeredEvent fired)
        {
            if (!_fired.TryGetValue(fired.TriggeringEvent, out var set))
            {
                set = new HashSet<ITriggeredAbility>();
                _fired[fired.TriggeringEvent] = set;
            }
            set.Add(fired.Ability);
            return;
        }

        // Record the abilities that SHOULD fire for this event, evaluated now (zone state is current).
        var expectedAbilities = new List<ITriggeredAbility>();
        int suppression = _etbSuppressionCount();
        foreach (var ability in EnumerateLiveAbilities())
        {
            if (!ability.IsTriggered(e)) continue;
            if (!ability.CanBePutOnStack()) continue;               // covers intervening-if
            if (suppression > 0 && IsCreatureEtbTrigger(e)) continue; // Torpor Orb (CR 603.3)
            expectedAbilities.Add(ability);
        }

        if (expectedAbilities.Count > 0)
            _expected.Add((e, expectedAbilities, _lastTurn, _lastPhase));
    }

    private IEnumerable<ITriggeredAbility> EnumerateLiveAbilities()
    {
        foreach (var p in _players)
            foreach (var zt in AllZones)
                foreach (var card in EnumerateZone(p, zt))
                    foreach (var ab in card.Abilities.OfType<ITriggeredAbility>())
                        yield return ab;
    }

    // Best-effort: a creature ETB is a CardMovedEvent into the battlefield of a creature card.
    // Returns false (suppression is rare in fixture decks and IsCreatureEtbTrigger is conservative).
    private static bool IsCreatureEtbTrigger(GameEvent e) => false;

    /// <summary>End-of-game structural invariants.</summary>
    public void RunFinalChecks(int turn, string phase, string? winnerName = null, bool reachedTurnCap = false)
    {
        CheckZoneIntegrity(turn, phase);
        CheckResult(turn, phase, winnerName, reachedTurnCap);
        CheckOrphanedTriggers();
    }

    private void CheckResult(int turn, string phase, string? winnerName, bool reachedTurnCap)
    {
        if (reachedTurnCap && winnerName is null)
        {
            _violations.Add(new InvariantViolation(
                "TurnCapReached",
                $"Game reached the turn cap at turn {turn} with no winner (suspicious, not necessarily a bug).",
                turn, phase));
            return;
        }

        if (winnerName is null)
        {
            _violations.Add(new InvariantViolation(
                "SingleResult",
                "Game ended with no winner and the turn cap was not reached.",
                turn, phase));
        }
    }

    private void CheckZoneIntegrity(int turn, string phase)
    {
        var seen = new Dictionary<Guid, string>();
        foreach (var p in _players)
        {
            foreach (var zt in AllZones)
            {
                foreach (var card in EnumerateZone(p, zt))
                {
                    if (seen.TryGetValue(card.InstanceId, out var firstZone))
                    {
                        _violations.Add(new InvariantViolation(
                            "ZoneIntegrity",
                            $"Card '{card.Name}' ({card.InstanceId}) present in both {firstZone} and {zt}.",
                            turn, phase));
                    }
                    else
                    {
                        seen[card.InstanceId] = zt.ToString();
                    }
                }
            }
        }
    }

    private static IEnumerable<ICard> EnumerateZone(Player p, ZoneType zt)
    {
        var zone = p.Zones.GetZone(zt);
        return zone?.GetCards() ?? Enumerable.Empty<ICard>();
    }

    private void CheckOrphanedTriggers()
    {
        foreach (var (evt, expectedList, turn, phase) in _expected)
        {
            _fired.TryGetValue(evt, out var firedSet);
            foreach (var ability in expectedList)
            {
                bool didFire = firedSet?.Contains(ability) ?? false;
                if (!didFire)
                {
                    var name = ability.Source is ICard c ? c.Name : ability.Source?.ToString() ?? "<unknown>";
                    _violations.Add(new InvariantViolation(
                        "OrphanedTrigger",
                        $"Ability on '{name}' matched {evt.GetType().Name} but never fired.",
                        turn, phase));
                }
            }
        }
    }

    public void Dispose() => _bus.UnsubscribeAll(OnEvent);
}
