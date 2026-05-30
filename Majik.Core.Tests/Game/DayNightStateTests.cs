using FluentAssertions;
using Majik.Core.Game;
using Xunit;

/// <summary>
/// Unit tests for <see cref="DayNightState"/> — the game-level day/night
/// designation state machine (CR 730, "Day and Night"). Tested in isolation
/// from the turn engine; the untap-step transition check (CR 502.2 / 730.2)
/// is driven directly with the previous turn's active-player spell count.
/// </summary>
public class DayNightStateTests
{
    // ---------------------------------------------------------------
    // CR 730.1 — the game starts with neither day nor night.
    // ---------------------------------------------------------------

    [Fact]
    public void GameStartsWithNeitherDesignation()
    {
        var state = new DayNightState();

        state.Designation.Should().Be(DayNightDesignation.Neither);
        state.IsNeither.Should().BeTrue();
        state.IsDay.Should().BeFalse();
        state.IsNight.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // CR 730.1 — "It becomes day"/"It becomes night" make the game gain
    // the designation. Once it has become day or night, it always has
    // exactly one designation from that point forward.
    // ---------------------------------------------------------------

    [Fact]
    public void BecomeDaySetsDayDesignation()
    {
        var state = new DayNightState();

        state.BecomeDay();

        state.IsDay.Should().BeTrue();
        state.IsNight.Should().BeFalse();
        state.IsNeither.Should().BeFalse();
    }

    [Fact]
    public void BecomeNightSetsNightDesignation()
    {
        var state = new DayNightState();

        state.BecomeNight();

        state.IsNight.Should().BeTrue();
        state.IsDay.Should().BeFalse();
        state.IsNeither.Should().BeFalse();
    }

    [Fact]
    public void BecomeDayWhileAlreadyDayIsNoOp()
    {
        var state = new DayNightState();
        state.BecomeDay();

        state.BecomeDay();

        state.IsDay.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // CR 730.1a — "day becomes night" / "night becomes day".
    // ---------------------------------------------------------------

    [Fact]
    public void DayCanBecomeNight()
    {
        var state = new DayNightState();
        state.BecomeDay();

        state.BecomeNight();

        state.IsNight.Should().BeTrue();
        state.IsDay.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // CR 502.2 / 730.2 — untap-step day/night check.
    //   730.2a: if it's DAY and the previous turn's active player cast
    //           NO spells that turn, it becomes night.
    //   730.2b: if it's NIGHT and the previous turn's active player cast
    //           TWO OR MORE spells that turn, it becomes day.
    //   730.2c: if it's NEITHER, the check doesn't happen.
    // The check returns true iff it changed the designation.
    // ---------------------------------------------------------------

    [Fact]
    public void Untap_Neither_NeverChanges()
    {
        var state = new DayNightState();

        state.CheckUntapTransition(previousActivePlayerSpellsCast: 0).Should().BeFalse();
        state.IsNeither.Should().BeTrue();

        state.CheckUntapTransition(previousActivePlayerSpellsCast: 5).Should().BeFalse();
        state.IsNeither.Should().BeTrue();
    }

    [Fact]
    public void Untap_Day_NoSpellsCast_BecomesNight()
    {
        var state = new DayNightState();
        state.BecomeDay();

        var changed = state.CheckUntapTransition(previousActivePlayerSpellsCast: 0);

        changed.Should().BeTrue();
        state.IsNight.Should().BeTrue();
    }

    [Fact]
    public void Untap_Day_OneSpellCast_StaysDay()
    {
        var state = new DayNightState();
        state.BecomeDay();

        var changed = state.CheckUntapTransition(previousActivePlayerSpellsCast: 1);

        changed.Should().BeFalse();
        state.IsDay.Should().BeTrue();
    }

    [Fact]
    public void Untap_Day_TwoSpellsCast_StaysDay()
    {
        // 730.2a only flips DAY→night on ZERO spells; two-or-more is the
        // NIGHT→day trigger, not a day-stays/day-flips trigger.
        var state = new DayNightState();
        state.BecomeDay();

        var changed = state.CheckUntapTransition(previousActivePlayerSpellsCast: 2);

        changed.Should().BeFalse();
        state.IsDay.Should().BeTrue();
    }

    [Fact]
    public void Untap_Night_TwoSpellsCast_BecomesDay()
    {
        var state = new DayNightState();
        state.BecomeNight();

        var changed = state.CheckUntapTransition(previousActivePlayerSpellsCast: 2);

        changed.Should().BeTrue();
        state.IsDay.Should().BeTrue();
    }

    [Fact]
    public void Untap_Night_ThreeSpellsCast_BecomesDay()
    {
        var state = new DayNightState();
        state.BecomeNight();

        var changed = state.CheckUntapTransition(previousActivePlayerSpellsCast: 3);

        changed.Should().BeTrue();
        state.IsDay.Should().BeTrue();
    }

    [Fact]
    public void Untap_Night_OneSpellCast_StaysNight()
    {
        var state = new DayNightState();
        state.BecomeNight();

        var changed = state.CheckUntapTransition(previousActivePlayerSpellsCast: 1);

        changed.Should().BeFalse();
        state.IsNight.Should().BeTrue();
    }

    [Fact]
    public void Untap_Night_NoSpellsCast_StaysNight()
    {
        var state = new DayNightState();
        state.BecomeNight();

        var changed = state.CheckUntapTransition(previousActivePlayerSpellsCast: 0);

        changed.Should().BeFalse();
        state.IsNight.Should().BeTrue();
    }
}
