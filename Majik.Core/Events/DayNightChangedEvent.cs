using Majik.Core.Game;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when the game's day/night designation changes (CR 730,
/// "Day and Night") — either via the untap-step check (CR 502.2 / 730.2)
/// or via an effect that makes it day or night (CR 730.1).
///
/// Subscribers include the daybound/nightbound transform logic (CR 702.145
/// "as it becomes day/night, transform …") and UI / log clients. Carries the
/// new designation; "becomes day" / "becomes night" is <see cref="NewDesignation"/>.
/// </summary>
public class DayNightChangedEvent : GameEvent
{
    /// <summary>The day/night designation the game now has.</summary>
    public DayNightDesignation NewDesignation { get; }

    public DayNightChangedEvent(DayNightDesignation newDesignation)
        : base(EventType.Triggered)
    {
        NewDesignation = newDesignation;
    }
}
