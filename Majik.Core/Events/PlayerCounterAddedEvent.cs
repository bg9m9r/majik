using Majik.Core.Counters;

namespace Majik.Core.Events;

/// <summary>
/// CR 122 / CR 614 — fired by
/// <see cref="Majik.Core.Services.PlayerCountersService.Add"/> after one or
/// more counters have been placed on a <see cref="Players.Player"/>
/// (poison — CR 704.5c; energy — CR 107.16; experience — CR 107.14; or a
/// generic player counter). The player-scoped twin of
/// <see cref="CounterAddedEvent"/>. Published AFTER all replacement effects
/// (Solemnity / Suncleanser "players can't get counters") have been applied
/// to the original intent, so <see cref="Amount"/> reflects the actual count
/// committed to the player. Only published when a non-zero placement landed
/// (CR 603.6 — the event fires on a successful commit), so "you get an
/// experience counter / poison counter" trigger riders stay silent when the
/// placement was prevented.
/// </summary>
public class PlayerCounterAddedEvent : GameEvent
{
    /// <summary>The player that received the counters.</summary>
    public Players.Player Player { get; }

    /// <summary>The kind of counter placed (Poison / Energy / Experience / …).</summary>
    public CounterType CounterType { get; }

    /// <summary>Post-replacement amount actually committed. Always &gt; 0
    /// (the event is only published when a non-zero placement landed).</summary>
    public int Amount { get; }

    public PlayerCounterAddedEvent(Players.Player player, CounterType type, int amount)
        : base(EventType.PlayerCounterAdded)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        CounterType = type ?? throw new ArgumentNullException(nameof(type));
        Amount = amount;
    }
}
