using Majik.Bot.Diagnostics;

namespace Majik.Server.Matches;

/// <summary>
/// <see cref="IBotDecisionSink"/> that appends each
/// <see cref="BotDecision"/> to the in-memory <see cref="MatchReplayBuffer"/>
/// for the captured match. One instance per match — matchId is closed
/// over at construction so the record call is a pure function of the
/// decision. Mirrors <see cref="SignalrBotDecisionSink"/>'s shape; the
/// two are composed by <see cref="MatchService.CreateBotMatchAsync"/>
/// when both the replay buffer and SignalR fan-out are wired.
///
/// <para>Faulty buffer must not abort the engine — the buffer itself
/// swallows its own exceptions, but we also wrap the call here to honor
/// the broader observer-sink contract.</para>
/// </summary>
public sealed class ReplayBufferBotDecisionSink : IBotDecisionSink
{
    private readonly Guid _matchId;
    private readonly MatchReplayBuffer _buffer;

    public ReplayBufferBotDecisionSink(Guid matchId, MatchReplayBuffer buffer)
    {
        _matchId = matchId;
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public void Record(BotDecision decision)
    {
        try
        {
            _buffer.RecordDecision(_matchId, decision);
        }
        catch
        {
            // Observer-sink contract: never throw. The buffer's append
            // path already logs internally.
        }
    }
}
