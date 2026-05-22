using Microsoft.Extensions.Logging;

namespace Majik.Bot.Diagnostics;

/// <summary>
/// <see cref="IBotDecisionSink"/> that forwards each decision to an
/// <see cref="ILogger"/> as a single structured Information-level entry.
/// Format:
/// <c>bot.decision type=Priority chosen='CastSpell:Lightning Bolt' score=4.20 alts=[Pass:0.00, PlayLand:Mountain:1.00] ctx=[manaAvailable=2, board=parity]</c>
/// </summary>
/// <remarks>
/// Uses a single structured log message so downstream log scrapers can
/// either grep the text form or destructure named properties. Swallows
/// exceptions from the underlying logger so a faulty subscriber cannot
/// abort the engine (mirrors the <see cref="BotPlayerAgent"/> onThinking
/// contract).
/// </remarks>
public sealed class LoggerBotDecisionSink : IBotDecisionSink
{
    private readonly ILogger _logger;

    public LoggerBotDecisionSink(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Record(BotDecision decision)
    {
        try
        {
            _logger.LogInformation(
                "bot.decision type={DecisionType} chosen={Chosen} score={Score:F2} alts={Alternatives} ctx={Context}",
                decision.DecisionType,
                decision.Chosen,
                decision.ChosenScore,
                FormatAlternatives(decision.Alternatives),
                FormatContext(decision.Context));
        }
        catch
        {
            // observer fault must not abort engine.
        }
    }

    private static string FormatAlternatives(IReadOnlyList<BotDecisionAlternative> alts)
    {
        if (alts.Count == 0) return "[]";
        return "[" + string.Join(", ", alts.Select(a => $"{a.Name}:{a.Score:F2}")) + "]";
    }

    private static string FormatContext(IReadOnlyDictionary<string, string> ctx)
    {
        if (ctx.Count == 0) return "[]";
        return "[" + string.Join(", ", ctx.Select(kv => $"{kv.Key}={kv.Value}")) + "]";
    }
}
