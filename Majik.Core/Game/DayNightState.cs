namespace Majik.Core.Game;

/// <summary>
/// The game-level day/night designation (CR 730 — "Day and Night").
/// CR 730.1 — the game starts with <see cref="Neither"/> designation; once
/// it has become day or night it always has exactly one of those from that
/// point forward.
/// </summary>
public enum DayNightDesignation
{
    /// <summary>CR 730.1 — the game has neither day nor night (the start state).</summary>
    Neither = 0,

    /// <summary>The game is day.</summary>
    Day = 1,

    /// <summary>The game is night.</summary>
    Night = 2,
}

/// <summary>
/// The game-level day/night state machine (CR 730, "Day and Night").
///
/// The game starts with <see cref="DayNightDesignation.Neither"/> (CR 730.1).
/// It becomes day or night through the daybound/nightbound keyword abilities
/// (CR 702.145) or other effects. Once it has a day/night designation it
/// keeps exactly one of the two from that point on — it can never return to
/// "neither".
///
/// The untap-step transition check (CR 502.2 / CR 730.2) is applied via
/// <see cref="CheckUntapTransition"/>, driven with the number of spells the
/// PREVIOUS turn's active player cast during that turn:
///   730.2a — if it's day and they cast no spells, it becomes night;
///   730.2b — if it's night and they cast two or more spells, it becomes day;
///   730.2c — if it's neither, the check doesn't happen.
///
/// Owned by the turn engine (<see cref="TurnDriver"/>), one per game.
/// </summary>
public sealed class DayNightState
{
    /// <summary>The current day/night designation (CR 730.1).</summary>
    public DayNightDesignation Designation { get; private set; } = DayNightDesignation.Neither;

    /// <summary>True iff the game currently has neither day nor night (CR 730.1).</summary>
    public bool IsNeither => Designation == DayNightDesignation.Neither;

    /// <summary>True iff the game is currently day.</summary>
    public bool IsDay => Designation == DayNightDesignation.Day;

    /// <summary>True iff the game is currently night.</summary>
    public bool IsNight => Designation == DayNightDesignation.Night;

    /// <summary>
    /// CR 730.1 — "it becomes day". The game gains the day designation
    /// (from neither, or from night via CR 730.1a "night becomes day").
    /// Idempotent when it's already day.
    /// </summary>
    /// <returns>True iff this call changed the designation to day.</returns>
    public bool BecomeDay()
    {
        if (IsDay) return false;
        Designation = DayNightDesignation.Day;
        return true;
    }

    /// <summary>
    /// CR 730.1 — "it becomes night". The game gains the night designation
    /// (from neither, or from day via CR 730.1a "day becomes night").
    /// Idempotent when it's already night.
    /// </summary>
    /// <returns>True iff this call changed the designation to night.</returns>
    public bool BecomeNight()
    {
        if (IsNight) return false;
        Designation = DayNightDesignation.Night;
        return true;
    }

    /// <summary>
    /// CR 502.2 / CR 730.2 — the untap-step day/night check, run as the
    /// second turn-based action of the untap step. Inspects the PREVIOUS
    /// turn's active player's spell count:
    ///   730.2a — day + zero spells cast → becomes night;
    ///   730.2b — night + two-or-more spells cast → becomes day;
    ///   730.2c — neither → no check.
    /// </summary>
    /// <param name="previousActivePlayerSpellsCast">
    /// Spells the previous turn's active player cast during that turn.
    /// </param>
    /// <returns>True iff this check changed the day/night designation.</returns>
    public bool CheckUntapTransition(int previousActivePlayerSpellsCast)
    {
        // CR 730.2c — no check while neither day nor night.
        if (IsNeither) return false;

        // CR 730.2a — day and no spells cast → becomes night.
        if (IsDay && previousActivePlayerSpellsCast == 0)
        {
            BecomeNight();
            return true;
        }

        // CR 730.2b — night and two or more spells cast → becomes day.
        if (IsNight && previousActivePlayerSpellsCast >= 2)
        {
            BecomeDay();
            return true;
        }

        return false;
    }
}
