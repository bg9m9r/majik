using System;
using System.Collections.Generic;
using System.Linq;
using Majik.Core.Cards;
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
        // Per-event checks added in later tasks.
    }

    /// <summary>End-of-game structural invariants.</summary>
    public void RunFinalChecks(int turn, string phase, string? winnerName = null, bool reachedTurnCap = false)
    {
        CheckZoneIntegrity(turn, phase);
        CheckResult(turn, phase, winnerName, reachedTurnCap);
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

    public void Dispose() => _bus.UnsubscribeAll(OnEvent);
}
