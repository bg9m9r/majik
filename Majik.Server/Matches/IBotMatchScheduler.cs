namespace Majik.Server.Matches;

/// <summary>
/// Drives the bot's pre-game actions (dice roll, play/draw choice) with a
/// brief wall-clock dwell between events so the SignalR-fed UI has time to
/// render the Rolling state and the dice values.
///
/// <para>Implementations call back into <see cref="MatchService"/> via the
/// existing public methods (<see cref="MatchService.SubmitRollAsync"/> and
/// <see cref="MatchService.PlayDrawAsync"/>) so the same SignalR events fire
/// identically to the human-vs-human path — the only difference for the
/// client is timing.</para>
///
/// <para>Tests inject <see cref="ImmediateBotMatchScheduler"/> (zero delay,
/// synchronous awaitable completion) so the full bot flow lands in the
/// asserted state before the next call returns; prod uses
/// <see cref="BotMatchScheduler"/> which fires-and-forgets on Task.Run with
/// real delays.</para>
/// </summary>
public interface IBotMatchScheduler
{
    /// <summary>Schedule the bot to submit its dice roll for the given match.
    /// Default prod implementation waits <c>RollDelay</c> before calling
    /// <see cref="MatchService.SubmitRollAsync"/> on a fresh DI scope.</summary>
    void ScheduleBotRoll(Guid matchId, string botSub);

    /// <summary>Schedule the bot to choose play/draw after it won the roll.
    /// Default prod implementation waits <c>PlayDrawDelay</c> before calling
    /// <see cref="MatchService.PlayDrawAsync"/> with <c>choice = "play"</c>
    /// on a fresh DI scope.</summary>
    void ScheduleBotPlayDraw(Guid matchId, string botSub);
}
