namespace Majik.Bot.Diagnostics;

/// <summary>
/// Receives structured <see cref="BotDecision"/> records emitted by bot
/// policies. Implementations must be thread-safe and non-throwing — a
/// faulty sink must not abort the engine. <see cref="NullBotDecisionSink"/>
/// is the prod default (zero overhead); the server wires a logger-backed
/// sink when <c>Bot:DecisionLogging:Enabled</c> is true.
/// </summary>
public interface IBotDecisionSink
{
    void Record(BotDecision decision);
}

/// <summary>No-op sink. Used when decision logging is disabled (default).</summary>
public sealed class NullBotDecisionSink : IBotDecisionSink
{
    public static readonly NullBotDecisionSink Instance = new();
    public void Record(BotDecision decision) { }
}
