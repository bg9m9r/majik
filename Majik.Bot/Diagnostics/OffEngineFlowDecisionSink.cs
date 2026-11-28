using Majik.Core.Game;

namespace Majik.Bot.Diagnostics;

/// <summary>
/// Decorates a consumer-supplied <see cref="IBotDecisionSink"/> so each
/// <see cref="Record"/> runs off the engine flow (<see cref="EngineFlow.RunExternal"/>):
/// the sink, and anything it schedules, is a foreign caller and reads the
/// published game-state snapshot rather than live objects.
/// </summary>
public sealed class OffEngineFlowDecisionSink : IBotDecisionSink
{
    private readonly IBotDecisionSink _inner;

    public OffEngineFlowDecisionSink(IBotDecisionSink inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public void Record(BotDecision decision) => EngineFlow.RunExternal(() => _inner.Record(decision));
}