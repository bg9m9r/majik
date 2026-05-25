using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// CR 702.131 — fired once, the first time a player reaches 10+ permanents
/// and gains the city's blessing for the rest of the game (Ascend). The
/// state is latched on <see cref="Player.HasCitysBlessing"/>; this event
/// exists so listeners (UI, triggers, EV) can react to the transition.
/// </summary>
public class GainedCitysBlessingEvent : GameEvent
{
    /// <summary>The player who just gained the city's blessing.</summary>
    public Player Player { get; }

    public GainedCitysBlessingEvent(Player player)
        : base(EventType.GainedCitysBlessing)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
