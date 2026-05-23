namespace Majik.Bot.Diagnostics;

/// <summary>
/// Fan-out <see cref="IBotDecisionSink"/> that forwards each
/// <see cref="BotDecision"/> to every wrapped sink in order. Used when
/// more than one observer wants the same decision stream — e.g. the
/// server logger AND a per-match SignalR publisher. Faulty inner sinks
/// must not abort the engine, so each callout is wrapped in a
/// try/catch (mirrors <see cref="LoggerBotDecisionSink"/>'s observer
/// contract).
/// </summary>
public sealed class CompositeBotDecisionSink : IBotDecisionSink
{
    private readonly IReadOnlyList<IBotDecisionSink> _sinks;

    public CompositeBotDecisionSink(IEnumerable<IBotDecisionSink> sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        _sinks = sinks.Where(s => s is not null and not NullBotDecisionSink).ToArray();
    }

    /// <summary>Convenience factory that filters nulls + NullBotDecisionSink
    /// instances. Returns <see cref="NullBotDecisionSink.Instance"/> if no
    /// real sink survives, or the single sink if only one survives — avoids
    /// composing a wrapper when there's nothing to compose.</summary>
    public static IBotDecisionSink Compose(params IBotDecisionSink?[] sinks)
    {
        var real = sinks
            .Where(s => s is not null and not NullBotDecisionSink)
            .Cast<IBotDecisionSink>()
            .ToArray();
        return real.Length switch
        {
            0 => NullBotDecisionSink.Instance,
            1 => real[0],
            _ => new CompositeBotDecisionSink(real),
        };
    }

    public void Record(BotDecision decision)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Record(decision);
            }
            catch
            {
                // Observer fault in one sink must not block other sinks
                // or abort the engine.
            }
        }
    }
}
