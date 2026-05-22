using System.Collections.Concurrent;

namespace Majik.Bot.Diagnostics;

/// <summary>
/// Thread-safe in-memory sink. Captures every recorded decision for later
/// inspection. Intended for tests and dev-tooling, not prod — unbounded
/// growth.
/// </summary>
public sealed class CapturingBotDecisionSink : IBotDecisionSink
{
    private readonly ConcurrentQueue<BotDecision> _decisions = new();

    public void Record(BotDecision decision) => _decisions.Enqueue(decision);

    public IReadOnlyList<BotDecision> Decisions => _decisions.ToArray();

    public IEnumerable<BotDecision> OfType(string decisionType)
        => _decisions.Where(d => d.DecisionType == decisionType);

    public void Clear()
    {
        while (_decisions.TryDequeue(out _)) { }
    }
}
